using System.Text.Json;
using System.Text.Json.Nodes;
using FeishuAdaptor.FeishuCard.CardViews;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FeishuAdaptor.FeishuCard;

/// <summary>
/// 封装一次 Agent 会话的飞书卡片事件订阅，将 EventBus 事件驱动为飞书卡片 UI。
/// <para>推理(reasoning)与工具调用合并进同一张流式卡(<see cref="MergeCardView"/>);
/// 文本输出(text)单独成卡，并作为合并卡的边界 —— 出现 text 即封口当前合并卡，之后的 reasoning+工具开新卡。</para>
/// </summary>
public class FeishuCardSession : IDisposable
{
    private readonly IServiceProvider _sp;
    private readonly string _userId;
    private readonly EventBus _bus;
    private readonly string _key;
    private readonly ILogger<FeishuCardSession>? _logger;
    private readonly List<IDisposable> _subs = [];

    // 所有事件回调通过 _gate 串行化(EventBus 用 Task.WhenAll 并发分发，并行工具也会并发触发)。
    private readonly SemaphoreSlim _gate = new(1, 1);

    // 父 Agent:合并卡(reasoning + 工具)
    private MergeCardView? _activeMergeCard;
    private readonly List<MergeCardView> _mergeCards = [];
    private readonly Dictionary<string, MergeCardView> _toolCallToCard = new();
    private bool _mergeSealed;
    private bool _hasActiveReasoning;

    // 父 Agent:文本输出卡(text)
    private CardView<LlmOutputViewModel>? _activeOutputCard;
    private readonly List<CardView<LlmOutputViewModel>> _outputCards = [];

    // 子 Agent 委托卡(维持现状)
    private DelegationCardView? _activeDelegationCard;
    private readonly List<IDisposable> _childSubs = [];

    public FeishuCardSession(IServiceProvider sp, string userId, EventBus bus, string key)
    {
        _sp = sp;
        _userId = userId;
        _bus = bus;
        _key = key;
        _logger = sp.GetService<ILogger<FeishuCardSession>>();
    }

    public void Subscribe()
    {
        _subs.Add(_bus.Subscribe<ModelContentEvent>(_key, OnModelContent));
        _subs.Add(_bus.Subscribe<BeforeToolExecuteEvent>(_key, OnBeforeToolExecute));
        _subs.Add(_bus.Subscribe<AfterToolExecuteEvent>(_key, OnAfterToolExecute));
        _subs.Add(_bus.Subscribe<AgentCompletedEvent>(_key, OnAgentCompleted));
        _subs.Add(_bus.Subscribe<SubAgentStartedEvent>(_key, OnSubAgentStarted));
        _subs.Add(_bus.Subscribe<SubAgentCompletedEvent>(_key, OnSubAgentCompleted));
    }

    // ─────────────── 父 Agent 事件 ───────────────

    private async Task OnModelContent(ModelContentEvent evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _logger?.LogInformation(
                "[Card] ModelContent Kind={Kind} TextLen={TextLen} sealed={Sealed} hasOutput={HasOutput} hasReasoning={HasReasoning}",
                evt.Kind, evt.Text?.Length ?? 0, _mergeSealed, _activeOutputCard is not null, _hasActiveReasoning);
            switch (evt.Kind)
            {
                case ModelContentKind.Reasoning:
                {
                    if (string.IsNullOrEmpty(evt.Text)) break;
                    EnsureMergeCard();
                    if (!_hasActiveReasoning)
                    {
                        _activeMergeCard!.EnqueueAppendReasoning();
                        _hasActiveReasoning = true;
                    }
                    _activeMergeCard!.EnqueueUpdateReasoning(evt.Text);
                    break;
                }
                case ModelContentKind.Text:
                {
                    if (string.IsNullOrEmpty(evt.Text)) break;
                    // text 是合并卡的边界:封口当前合并卡,之后的 reasoning+工具开新卡,
                    // 避免 text 独立卡夹在中间、旧合并卡又向上追加造成显示错乱。
                    SealActiveMergeCard();
                    if (_activeOutputCard is null)
                    {
                        var (_, view) = CreateCard<LlmOutputViewModel>();
                        _activeOutputCard = view;
                        _outputCards.Add(view);
                    }
                    _activeOutputCard!.ViewModel.Output += evt.Text;
                    break;
                }
                case ModelContentKind.Completed:
                {
                    // Completed 只在整个 turn 结束时发(非每轮 LLM,见 EventPublishingMiddleware),
                    // 此处仅作兜底关闭 text 卡;轮次间的 text 分隔由工具边界(OnBeforeToolExecute)处理。
                    await CloseActiveOutputCardAsync(ct);
                    _hasActiveReasoning = false;
                    break;
                }
            }
        }
        finally { _gate.Release(); }
    }

    private async Task OnBeforeToolExecute(BeforeToolExecuteEvent evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _logger?.LogInformation("[Card] BeforeTool Tool={Tool} CallId={CallId}", evt.ToolName, evt.CallId);

            // 任何工具开始都意味着上一段 text 结束 —— Completed 只在 turn 结束发(非每轮),
            // 无法分隔轮次,故在工具边界显式关闭 text 卡,确保工具后的 text 新开卡。
            await CloseActiveOutputCardAsync(ct);

            // DelegateToAgent → 子 Agent 委托卡(维持现状;作为边界封口合并卡)
            if (evt.ToolName == "DelegateToAgent")
            {
                SealActiveMergeCard();

                var delegationView = (DelegationCardView)_sp.GetRequiredService<CardView<DelegationViewModel>>();
                await delegationView.InitializeAsync(ct);
                await delegationView.SendToUserAsync("user_id", _userId, ct);

                string agentName = "";
                string task = "";
                if (evt.ArgumentsJson is not null)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(evt.ArgumentsJson);
                        if (doc.RootElement.TryGetProperty("agentName", out var nameProp))
                            agentName = nameProp.GetString() ?? "";
                        if (doc.RootElement.TryGetProperty("task", out var taskProp))
                            task = taskProp.GetString() ?? "";
                    }
                    catch { }
                }

                await delegationView.UpdateForStartAsync(agentName, task, ct);
                _activeDelegationCard = delegationView;
                return;
            }

            // 普通工具 → 合并卡
            EnsureMergeCard();
            _hasActiveReasoning = false; // 工具块出现,当前推理块结束
            var (purpose, cleanArgs) = ExtractPurpose(evt.ArgumentsJson);
            _activeMergeCard!.EnqueueAppendTool(evt.CallId, evt.ToolName ?? "未知工具", cleanArgs, purpose);
            _toolCallToCard[evt.CallId] = _activeMergeCard;
        }
        finally { _gate.Release(); }
    }

    private async Task OnAfterToolExecute(AfterToolExecuteEvent evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // DelegateToAgent 完成 → 释放子 Agent 状态
            if (evt.ToolName == "DelegateToAgent" && _activeDelegationCard is not null)
            {
                await _activeDelegationCard.UpdateForCompletedAsync(ct);
                DisposeChildSubs();
                _activeDelegationCard = null;
                return;
            }

            // 普通工具结果 → 路由到注册该 callId 的那张合并卡(可能已被 text/委托打断、不再是当前活动卡)
            if (!_toolCallToCard.TryGetValue(evt.CallId, out var card)) return;

            var resultText = evt.Error ?? evt.ResultJson ?? "";
            if (resultText.Length > 500)
                resultText = string.Concat(resultText.AsSpan(0, 500), "\n...");

            card.EnqueueUpdateToolResult(
                evt.CallId,
                string.IsNullOrWhiteSpace(resultText) ? "无返回结果" : resultText,
                isError: evt.Error is not null);
        }
        finally { _gate.Release(); }
    }

    private async Task OnAgentCompleted(AgentCompletedEvent evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await CloseAllAsync(ct); }
        finally { _gate.Release(); }
    }

    // ─────────────── 合并卡 / 文本卡 辅助 ───────────────

    /// <summary>确保存在未封口的活动合并卡;否则新建一张。</summary>
    private void EnsureMergeCard()
    {
        if (_activeMergeCard is not null && !_mergeSealed) return;
        _activeMergeCard = CreateMergeCard();
        _mergeCards.Add(_activeMergeCard);
        _mergeSealed = false;
        _hasActiveReasoning = false;
    }

    /// <summary>封口当前活动合并卡(text / 委托作为边界时调用),之后新内容开新卡。</summary>
    private void SealActiveMergeCard()
    {
        if (_activeMergeCard is null || _mergeSealed) return;
        _mergeSealed = true;
        _hasActiveReasoning = false;
    }

    /// <summary>关闭并清空当前 text 卡(若存在)。工具边界 / turn 结束时调用。</summary>
    private async Task CloseActiveOutputCardAsync(CancellationToken ct)
    {
        if (_activeOutputCard is null) return;
        try { await _activeOutputCard.CloseStreamingAsync(ct); }
        catch { }
        _activeOutputCard = null;
    }

    private MergeCardView CreateMergeCard()
    {
        var card = _sp.GetRequiredService<MergeCardView>();
        card.InitializeAsync().GetAwaiter().GetResult();
        card.SendToUserAsync("user_id", _userId).GetAwaiter().GetResult();
        return card;
    }

    private async Task CloseAllAsync(CancellationToken ct)
    {
        foreach (var card in _mergeCards)
        {
            try { await card.CloseStreamingAsync(ct); }
            catch { }
            card.Dispose();
        }
        _mergeCards.Clear();
        _activeMergeCard = null;
        _toolCallToCard.Clear();

        if (_activeOutputCard is not null)
        {
            try { await _activeOutputCard.CloseStreamingAsync(ct); }
            catch { }
            _activeOutputCard = null;
        }
    }

    // ─────────────── 子 Agent 事件(维持现状,仅补 _gate 保护)───────────────

    private async Task OnSubAgentStarted(SubAgentStartedEvent evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // 订阅子 Agent 的事件(以子 Agent 的 AgentId 为 key)
            _childSubs.Add(_bus.Subscribe<ModelContentEvent>(evt.SubAgentId, OnChildModelContent));
            _childSubs.Add(_bus.Subscribe<BeforeToolExecuteEvent>(evt.SubAgentId, OnChildBeforeToolExecute));
            _childSubs.Add(_bus.Subscribe<AfterToolExecuteEvent>(evt.SubAgentId, OnChildAfterToolExecute));
        }
        finally { _gate.Release(); }
    }

    private async Task OnSubAgentCompleted(SubAgentCompletedEvent evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // 最终 FlushAsync(将累积的文本一次性刷到卡片)
            if (_activeDelegationCard is not null)
                await _activeDelegationCard.UpdateForCompletedAsync(ct);
            DisposeChildSubs();
            _activeDelegationCard = null;
        }
        finally { _gate.Release(); }
    }

    private async Task OnChildModelContent(ModelContentEvent evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_activeDelegationCard is null) return;
            switch (evt.Kind)
            {
                case ModelContentKind.Reasoning:
                    if (!string.IsNullOrEmpty(evt.Text))
                        await _activeDelegationCard.AppendReasoningAsync(evt.Text, ct);
                    break;
                case ModelContentKind.Text:
                    if (!string.IsNullOrEmpty(evt.Text))
                        await _activeDelegationCard.AppendOutputAsync(evt.Text, ct);
                    break;
                case ModelContentKind.Completed:
                    // 子 Agent 文本输出结束,FlushAsync 累积的文本
                    await _activeDelegationCard.FlushAsync(ct);
                    break;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task OnChildBeforeToolExecute(BeforeToolExecuteEvent evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_activeDelegationCard is null) return;
            var (purpose, cleanArgs) = ExtractPurpose(evt.ArgumentsJson);
            await _activeDelegationCard.AddChildToolStartAsync(
                evt.CallId,
                evt.ToolName ?? "未知工具",
                cleanArgs,
                purpose,
                ct);
        }
        finally { _gate.Release(); }
    }

    private async Task OnChildAfterToolExecute(AfterToolExecuteEvent evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_activeDelegationCard is null) return;

            var resultText = evt.Error ?? evt.ResultJson ?? "";
            if (resultText.Length > 500)
                resultText = string.Concat(resultText.AsSpan(0, 500), "\n...");

            await _activeDelegationCard.UpdateChildToolResultAsync(
                evt.CallId,
                string.IsNullOrWhiteSpace(resultText) ? "无返回结果" : resultText,
                evt.Error is not null,
                ct);
        }
        finally { _gate.Release(); }
    }

    // ─────────────── 通用辅助 ───────────────

    /// <summary>从工具参数 JSON 中提取 purpose 字段(工具调用意图),并将其从剩余参数中移除避免重复展示。</summary>
    private static (string purpose, string cleanArguments) ExtractPurpose(string? argumentsJson)
    {
        if (string.IsNullOrEmpty(argumentsJson))
            return ("", "无参数");

        try
        {
            var node = JsonNode.Parse(argumentsJson)?.AsObject();
            if (node is null)
                return ("", argumentsJson);

            var purpose = "";
            if (node.TryGetPropertyValue("purpose", out var purposeNode) && purposeNode is not null)
                purpose = purposeNode.GetValue<string>();

            node.Remove("purpose");

            var cleanJson = node.Count > 0 ? node.ToJsonString() : "无参数";
            return (purpose, cleanJson);
        }
        catch
        {
            return ("", argumentsJson ?? "无参数");
        }
    }

    private (T ViewModel, CardView<T> View) CreateCard<T>() where T : ViewModelBase
    {
        var view = _sp.GetRequiredService<CardView<T>>();
        view.InitializeAsync().GetAwaiter().GetResult();
        view.SendToUserAsync("user_id", _userId).GetAwaiter().GetResult();
        return (view.ViewModel, view);
    }

    private void DisposeChildSubs()
    {
        foreach (var sub in _childSubs) sub.Dispose();
        _childSubs.Clear();
    }

    public void Dispose()
    {
        // 兜底关闭(AgentCompletedEvent 未触发时,如异常中断)
        try { CloseAllAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { }

        foreach (var card in _outputCards)
        {
            try { card.Dispose(); }
            catch { }
        }
        _outputCards.Clear();

        DisposeChildSubs();
        foreach (var sub in _subs) sub.Dispose();
        _gate.Dispose();
    }
}
