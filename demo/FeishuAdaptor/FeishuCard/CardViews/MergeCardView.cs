using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FeishuAdaptor.FeishuCard.Cards;
using ManInBlack.AI.Abstraction.Attributes;
using Microsoft.Extensions.Logging;

namespace FeishuAdaptor.FeishuCard.CardViews;

/// <summary>
/// 合并卡片 — 把一次 Agent 回复中连续的「推理 + 工具调用」合并进同一张流式卡。
/// <para>所有小块（推理折叠面板、工具折叠面板）都装进一个「大折叠面板」:生成过程中大面板展开(标题随机,营造工作中感),
/// turn 结束时局部更新大面板为折叠 + 关闭流式。</para>
/// <para>并发安全：<see cref="EventBus"/> 回调并发进入,所有状态与飞书 API 调用都在单消费者线程串行执行。</para>
/// </summary>
[ServiceRegister.Transient]
public class MergeCardView : CardViewBase
{
    private readonly CardService _cardService;
    private readonly ILogger<MergeCardView> _logger;

    private volatile string? _cardId;
    private string? _rootPanelElementId;
    private string _title = "";
    private int _sequence;

    /// <summary>卡片实体 ID（由 <see cref="InitializeAsync"/> 创建后填充）。</summary>
    public string CardId =>
        _cardId ?? throw new InvalidOperationException($"卡片未创建，请先调用 {nameof(InitializeAsync)}。");

    private readonly Channel<CardOp> _channel = Channel.CreateUnbounded<CardOp>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
    );
    private readonly CancellationTokenSource _cts = new();
    private Task? _consumer;
    private int _disposed; // Dispose 幂等守卫(实例会被 DI scope 与 FeishuCardSession 各 dispose 一次)

    // ─────────────── 消费者线程独占状态（只由 ConsumeAsync 访问）───────────────
    private readonly List<ReasoningBlockState> _reasoningBlocks = new();
    private int _activeReasoningIndex = -1;
    private readonly Dictionary<string, ToolBlockState> _tools = new();
    private readonly List<AppendItem> _pendingAppend = new();
    private readonly Dictionary<string, ContentItem> _pendingContent = new();
    private readonly Dictionary<string, ReplaceItem> _pendingReplace = new();

    /// <summary>生成过程中大面板的随机标题(戏精卖力工作中态,梗感拉满)。</summary>
    private static readonly string[] WorkTitles =
    [
        "🤯 绞尽脑汁中...", "🔥 燃烧脑细胞...", "🫠 CPU 冒烟了...", "🧠 搜肠刮肚...",
        "⚡ 算力全开...", "🛠️ 敲敲打打...", "🐮 牛马打工中...", "🥵 满头大汗...",
        "💪 肝帝附体...", "🏃 拼了老命...", "🧑‍💻 猛敲键盘...", "🤔 让我想想...",
        "🔨 锤炼答案中...", "🐔 卷起来了...", "🥋 硬控思考...", "💭 让子弹飞...",
    ];

    /// <summary>turn 收尾大面板的随机标题(戏精卖力完成态,与 <see cref="WorkTitles"/> 呼应)。</summary>
    private static readonly string[] DoneTitles =
    [
        "😮‍💨 长舒一口气", "😎 拿下!", "🎉 搞定!", "✌️ 我真棒!",
        "🫠 累但完事了", "✅ 交差!", "🤙 拿捏了!", "🎊 芜湖起飞~",
        "🏆 yyds!", "😎 游刃有余", "🎈 收工!", "😮‍💨 终于搞完",
        "🥂 敬自己一杯", "💪 轻松拿下", "✨ 魔法完成", "🫡 完美谢幕",
    ];

    public MergeCardView(CardService cardService, ILogger<MergeCardView> logger)
    {
        _cardService = cardService;
        _logger = logger;
    }

    // ─────────────── 生命周期 ───────────────

    /// <summary>创建卡片实体(大面板展开、随机工作中标题)并启动后台消费者。</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _rootPanelElementId = NextElementId();
        _title = WorkTitles[Random.Shared.Next(WorkTitles.Length)];
        _cardId = await _cardService.CreateAsync(BuildFullCard(), ct);
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>把卡片作为 interactive 消息发给指定用户。</summary>
    public Task SendToUserAsync(string userIdType, string userId, CancellationToken ct = default) =>
        _cardService.SendMessageAsync(CardId, userIdType, userId, ct);

    // ─────────────── 生产者:事件回调入队(线程安全)───────────────

    public void EnqueueAppendReasoning() => Write(new AppendReasoningOp());

    public void EnqueueUpdateReasoning(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Write(new UpdateReasoningOp(text));
    }

    public void EnqueueAppendTool(string callId, string toolName, string args, string description) =>
        Write(new AppendToolOp(callId, toolName, args, description));

    public void EnqueueUpdateToolResult(string callId, string result, bool isError) =>
        Write(new UpdateToolResultOp(callId, result, isError));

    /// <summary>turn 结束:局部更新大面板为折叠 + 关闭流式,等待消费者处理完成。</summary>
    public override async Task CloseStreamingAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        _channel.Writer.TryWrite(new CloseStreamingOp(tcs));
        await tcs.Task;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        // 幂等守卫:MergeCardView 是 tracked transient,会被 DI scope 和 FeishuCardSession 各 dispose 一次,
        // 第二次进来若再 _cts.Cancel() 会抛 ObjectDisposedException。
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _channel.Writer.TryComplete();
        _cts.Cancel();
        try { _consumer?.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch { /* 消费者关闭异常忽略 */ }
        _cts.Dispose();
    }

    // ─────────────── 消费者 ───────────────

    private async Task ConsumeAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                CloseStreamingOp? closeOp = null;
                while (_channel.Reader.TryRead(out var op))
                {
                    if (op is CloseStreamingOp c)
                        closeOp = c;
                    else
                        ApplyOp(op);
                }

                await FlushPendingAsync();

                if (closeOp is not null)
                {
                    // 局部更新收尾:只折叠根面板 + 改收尾标题/配色(小块内容流式时已更新,无需全量重传)。
                    // 实测「更新组件属性」接口可改 expanded 与 header(2026-07-10 验证),故弃用 FullUpdate。
                    // try/finally 保证 tcs 一定释放:收尾 API 失败时不能让 CloseStreamingAsync 永久挂起
                    // (它被 FeishuCardSession.Dispose 同步 await,挂起会卡死整条消息处理)。
                    try
                    {
                        await _cardService.PartialUpdateElementAsync(
                            CardId, _rootPanelElementId!, BuildRootPanelClosePartial(), GetNextSequence(), _cts.Token);
                        // 显式退出流式模式:settings patch 确保飞书侧真正结束流式(否则卡片可能停留在"进行中/展开"状态)。
                        await _cardService.CloseStreamingAsync(CardId, GetNextSequence(), _cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "MergeCardView 收尾折叠失败 CardId={CardId}", _cardId);
                    }
                    finally
                    {
                        closeOp.Tcs.TrySetResult();
                    }
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MergeCardView 消费者异常 CardId={CardId}", _cardId);
        }
    }

    private void ApplyOp(CardOp op)
    {
        switch (op)
        {
            case AppendReasoningOp:
                {
                    var markdownId = NextElementId();
                    _reasoningBlocks.Add(new ReasoningBlockState(markdownId));
                    _activeReasoningIndex = _reasoningBlocks.Count - 1;
                    _pendingAppend.Add(new AppendItem(GetNextSequence(), _rootPanelElementId!, BuildReasoningPanel(markdownId, "")));
                    break;
                }
            case UpdateReasoningOp ur when _activeReasoningIndex >= 0:
                {
                    var block = _reasoningBlocks[_activeReasoningIndex];
                    block.Content.Append(ur.Text);
                    _pendingContent[block.ElementId] = new ContentItem(GetNextSequence(), block.ElementId, block.Content.ToString());
                    break;
                }
            case AppendToolOp at:
                {
                    _activeReasoningIndex = -1; // 工具块出现,当前推理块结束
                    var panelId = NextElementId();
                    _tools[at.CallId] = new ToolBlockState(panelId, at.ToolName, at.Args, at.Description);
                    _pendingAppend.Add(new AppendItem(
                        GetNextSequence(),
                        _rootPanelElementId!,
                        BuildToolPanel(panelId, at.ToolName, at.Args, at.Description, "⏳ 执行中...", isError: false, isRunning: true)));
                    break;
                }
            case UpdateToolResultOp utr when _tools.TryGetValue(utr.CallId, out var tb):
                {
                    tb.Result = utr.Result;
                    tb.IsError = utr.IsError;
                    var panel = BuildToolPanel(tb.PanelElementId, tb.ToolName, tb.Args, tb.Description, utr.Result, utr.IsError, isRunning: false);
                    _pendingReplace[tb.PanelElementId] = new ReplaceItem(GetNextSequence(), tb.PanelElementId, panel);
                    break;
                }
        }
    }

    private async Task FlushPendingAsync()
    {
        if (_pendingAppend.Count == 0 && _pendingContent.Count == 0 && _pendingReplace.Count == 0)
            return;

        var sends = new List<SendItem>(_pendingAppend.Count + _pendingContent.Count + _pendingReplace.Count);
        sends.AddRange(_pendingAppend);
        sends.AddRange(_pendingContent.Values);
        sends.AddRange(_pendingReplace.Values);
        _pendingAppend.Clear();
        _pendingContent.Clear();
        _pendingReplace.Clear();

        foreach (var s in sends.OrderBy(x => x.Seq))
        {
            try { await s.SendAsync(_cardService, CardId, _cts.Token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MergeCardView 发送更新失败 CardId={CardId} Seq={Seq}", CardId, s.Seq);
            }
        }
    }

    // ─────────────── 卡片构建 ───────────────

    /// <summary>turn 收尾局部更新根面板的 partial_element JSON:折叠 + 随机收尾标题 + 绿色配色。</summary>
    /// <remarks>header 为嵌套对象,patch 时整体覆盖,故 icon 等字段需一并带上以免丢失。</remarks>
    private static string BuildRootPanelClosePartial()
    {
        var title = DoneTitles[Random.Shared.Next(DoneTitles.Length)];
        // 匿名对象序列化:snake_case + 忽略 null,且顶层不带 tag(满足 patch partial_element 不可改 tag 的约束)。
        var partial = new
        {
            Expanded = false,
            Header = new
            {
                Title = new { Tag = "plain_text", Content = title },
                BackgroundColor = "green-100",
                Icon = new { Tag = "standard_icon", Token = "down-bold_outlined" },
                IconPosition = "right",
                IconExpandedAngle = -180,
            },
        };
        return JsonSerializer.Serialize(partial, CardJsonSerializerOptions.Options);
    }

    /// <summary>创建初始卡片:大面板展开 + 流式开启 + 随机工作中标题。小块由消费者流式追加,不在创建时构建。</summary>
    private Card BuildFullCard()
    {
        var rootPanel = new CollapsiblePanelElement
        {
            ElementId = _rootPanelElementId!,
            Expanded = true,
            Header = new CollapsiblePanelHeader
            {
                Title = new TextElement { Content = _title },
                Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
                BackgroundColor = "grey-100",
                IconPosition = "right",
                IconExpandedAngle = -180,
            },
        };

        return new Card
        {
            Schema = "2.0",
            Config = new CardConfig
            {
                StreamingMode = true,
                EnableForward = true,
                EnableForwardInteraction = true,
            },
            Body = new CardBody { Elements = { rootPanel } },
        };
    }

    private static CollapsiblePanelElement BuildReasoningPanel(string markdownElementId, string content) => new()
    {
        Expanded = false,
        Elements = { new MarkdownElement { ElementId = markdownElementId, Content = content } },
        Header = new CollapsiblePanelHeader
        {
            Title = new TextElement { Content = "🤔 琢磨琢磨" },
            Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
            BackgroundColor = "lime-300",
            IconPosition = "right",
            IconExpandedAngle = -180,
        },
    };

    private static CollapsiblePanelElement BuildToolPanel(
        string panelElementId, string toolName, string args, string description,
        string resultContent, bool isError, bool isRunning)
    {
        var displayName = ToolDisplayNames.Get(toolName);
        if (!string.IsNullOrWhiteSpace(description))
            displayName = $"{displayName} - {description}";

        var title = isRunning ? $"{displayName} 中..." : (isError ? $"{displayName} 失败" : $"{displayName} 完成");
        var background = isRunning ? "indigo-100" : (isError ? "red-100" : "green-100");

        return new CollapsiblePanelElement
        {
            ElementId = panelElementId,
            Expanded = false,
            Elements =
            {
                new MarkdownElement { Content = string.IsNullOrWhiteSpace(args) ? "无参数" : args },
                new HrElement(),
                new MarkdownElement { Content = resultContent },
            },
            Header = new CollapsiblePanelHeader
            {
                Title = new TextElement { Content = title },
                Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
                BackgroundColor = background,
                IconPosition = "right",
                IconExpandedAngle = -180,
            },
        };
    }

    private void Write(CardOp op) => _channel.Writer.TryWrite(op);
    private int GetNextSequence() => Interlocked.Increment(ref _sequence);

    // ─────────────── 内部类型 ───────────────

    private abstract record CardOp;
    private sealed record AppendReasoningOp : CardOp;
    private sealed record UpdateReasoningOp(string Text) : CardOp;
    private sealed record AppendToolOp(string CallId, string ToolName, string Args, string Description) : CardOp;
    private sealed record UpdateToolResultOp(string CallId, string Result, bool IsError) : CardOp;
    private sealed record CloseStreamingOp(TaskCompletionSource Tcs) : CardOp;

    private sealed class ReasoningBlockState(string elementId)
    {
        public string ElementId { get; } = elementId;
        public StringBuilder Content { get; } = new();
    }

    private sealed class ToolBlockState(string panelElementId, string toolName, string args, string description)
    {
        public string PanelElementId { get; } = panelElementId;
        public string ToolName { get; } = toolName;
        public string Args { get; } = args;
        public string Description { get; } = description;
        public string? Result { get; set; }
        public bool IsError { get; set; }
    }

    private abstract record SendItem(int Seq)
    {
        public abstract Task SendAsync(CardService cs, string cardId, CancellationToken ct);
    }

    private sealed record AppendItem(int Seq, string TargetElementId, CardElement Element) : SendItem(Seq)
    {
        public override Task SendAsync(CardService cs, string cardId, CancellationToken ct) =>
            cs.AddElementsAsync(cardId, "append", TargetElementId, new[] { Element }, Seq, ct);
    }

    private sealed record ContentItem(int Seq, string ElementId, string Content) : SendItem(Seq)
    {
        public override Task SendAsync(CardService cs, string cardId, CancellationToken ct) =>
            cs.UpdateElementStreamAsync(cardId, ElementId, Content, Seq, ct);
    }

    private sealed record ReplaceItem(int Seq, string ElementId, CardElement Element) : SendItem(Seq)
    {
        public override Task SendAsync(CardService cs, string cardId, CancellationToken ct) =>
            cs.ReplaceElementAsync(cardId, ElementId, Element, Seq, ct);
    }
}
