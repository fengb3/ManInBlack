using System.Text;
using System.Threading.Channels;
using FeishuAdaptor.FeishuCard.Cards;
using ManInBlack.AI.Abstraction.Attributes;
using Microsoft.Extensions.Logging;

namespace FeishuAdaptor.FeishuCard.CardViews;

/// <summary>
/// 合并卡片 — 把一次 Agent 回复中连续的「推理 + 工具调用」合并进同一张流式卡。
/// <para>所有小块（推理折叠面板、工具折叠面板）都装进一个「大折叠面板」:生成过程中大面板展开(标题随机,营造工作中感),
/// turn 结束时全量重建为大面板折叠 + 流式关闭。</para>
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

    // ─────────────── 消费者线程独占状态（只由 ConsumeAsync 访问）───────────────
    private readonly List<ReasoningBlockState> _reasoningBlocks = new();
    private int _activeReasoningIndex = -1;
    private readonly Dictionary<string, ToolBlockState> _tools = new();
    private readonly List<string> _toolOrder = new(); // callId 顺序,用于全量重建
    private readonly List<AppendItem> _pendingAppend = new();
    private readonly Dictionary<string, ContentItem> _pendingContent = new();
    private readonly Dictionary<string, ReplaceItem> _pendingReplace = new();

    private static readonly string[] WorkTitles =
        ["🧠 思考中...", "🔍 分析中...", "⚙️ 处理中...", "✨ 整理思路...", "🤔 琢磨中...", "🛠️ 工作中..."];

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
        _cardId = await _cardService.CreateAsync(BuildFullCard(expanded: true, streamingDone: false), ct);
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

    /// <summary>turn 结束:全量重建(大面板折叠 + 流式关闭),等待消费者处理完成。</summary>
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
                    // 全量重建:大面板 expanded=false + 所有小块定稿。
                    // 用 FullUpdate 而非 PartialUpdate,因为 patch 接口不支持改 expanded。
                    var card = BuildFullCard(expanded: false, streamingDone: true);
                    await _cardService.FullUpdateAsync(CardId, card, GetNextSequence(), _cts.Token);
                    // 显式退出流式模式:FullUpdate 里带 streaming_mode:false 不一定能把"已进入流式"的卡切回,
                    // 需追加 settings patch 确保飞书侧真正结束流式(否则卡片可能停留在"进行中/展开"状态)。
                    await _cardService.CloseStreamingAsync(CardId, GetNextSequence(), _cts.Token);
                    closeOp.Tcs.TrySetResult();
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
                _toolOrder.Add(at.CallId);
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

    /// <summary>全量重建整张卡:大面板(可折叠)+ 所有小块(按生成顺序)的最新状态。</summary>
    private Card BuildFullCard(bool expanded, bool streamingDone)
    {
        var rootPanel = new CollapsiblePanelElement
        {
            ElementId = _rootPanelElementId!,
            Expanded = expanded,
            Header = new CollapsiblePanelHeader
            {
                Title = new TextElement { Content = streamingDone ? "✅ 思考与工具调用" : _title },
                Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
                BackgroundColor = streamingDone ? "green-100" : "grey-100",
                IconPosition = "right",
                IconExpandedAngle = -180,
            },
        };

        foreach (var rb in _reasoningBlocks)
            rootPanel.Elements.Add(BuildReasoningPanel(rb.ElementId, rb.Content.ToString()));
        foreach (var callId in _toolOrder)
        {
            var tb = _tools[callId];
            rootPanel.Elements.Add(BuildToolPanel(
                tb.PanelElementId, tb.ToolName, tb.Args, tb.Description,
                tb.Result ?? "⏳ 执行中...", tb.IsError, isRunning: tb.Result is null));
        }

        return new Card
        {
            Schema = "2.0",
            Config = new CardConfig
            {
                StreamingMode = !streamingDone, // 生成中 true / 完成 false
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
