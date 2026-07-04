using System.Text.Json;
using System.Text.Json.Nodes;
using FeishuAdaptor.FeishuCard.CardViews;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FeishuAdaptor.FeishuCard;

/// <summary>
/// 封装一次 Agent 会话的飞书卡片事件订阅，将 EventBus 事件驱动为飞书卡片 UI。
/// </summary>
public class FeishuCardSession : IDisposable
{
    private readonly IServiceProvider _sp;
    private readonly string _userId;
    private readonly EventBus _bus;
    private readonly string _key;
    private readonly List<IDisposable> _subs = [];

    // 状态跟踪
    private string _lastLlmType = "";
    private LlmOutputViewModel? _lastOutput;
    private LlmReasoningViewModel? _lastReasoning;
    private readonly List<CardViewBase> _streamingCardViews = [];
    private readonly Dictionary<string, ToolExecutionCardView> _toolExecutions = [];

    // 子 Agent 状态
    private DelegationCardView? _activeDelegationCard;
    private readonly List<IDisposable> _childSubs = [];

    public FeishuCardSession(IServiceProvider sp, string userId, EventBus bus, string key)
    {
        _sp = sp;
        _userId = userId;
        _bus = bus;
        _key = key;
    }

    public void Subscribe()
    {
        _subs.Add(_bus.Subscribe<ModelContentEvent>(_key, OnModelContent));
        _subs.Add(_bus.Subscribe<BeforeToolExecuteEvent>(_key, OnBeforeToolExecute));
        _subs.Add(_bus.Subscribe<AfterToolExecuteEvent>(_key, OnAfterToolExecute));
        _subs.Add(_bus.Subscribe<SubAgentStartedEvent>(_key, OnSubAgentStarted));
        _subs.Add(_bus.Subscribe<SubAgentCompletedEvent>(_key, OnSubAgentCompleted));
    }

    private async Task OnModelContent(ModelContentEvent evt, CancellationToken ct)
    {
        switch (evt.Kind)
        {
            case ModelContentKind.Reasoning:
            {
                if (string.IsNullOrEmpty(evt.Text)) break;
                if (_lastLlmType != nameof(LlmReasoningViewModel))
                {
                    var (vm, view) = CreateCard<LlmReasoningViewModel>();
                    _streamingCardViews.Add(view);
                    _lastReasoning = vm;
                    _lastLlmType = nameof(LlmReasoningViewModel);
                }
                _lastReasoning!.Reasoning += evt.Text;
                break;
            }
            case ModelContentKind.Text:
            {
                if (string.IsNullOrEmpty(evt.Text)) break;
                if (_lastLlmType != nameof(LlmOutputViewModel))
                {
                    var (vm, view) = CreateCard<LlmOutputViewModel>();
                    _streamingCardViews.Add(view);
                    _lastOutput = vm;
                    _lastLlmType = nameof(LlmOutputViewModel);
                }
                _lastOutput!.Output += evt.Text;
                break;
            }
            case ModelContentKind.Completed:
            {
                foreach (var view in _streamingCardViews)
                {
                    try { await view.CloseStreamingAsync(ct); }
                    catch { }
                }
                break;
            }
        }
    }

    private async Task OnBeforeToolExecute(BeforeToolExecuteEvent evt, CancellationToken ct)
    {
        _lastLlmType = "";

        // DelegateToAgent 工具 → 创建 DelegationCardView
        if (evt.ToolName == "DelegateToAgent")
        {
            var delegationView = (DelegationCardView)_sp.GetRequiredService<CardView<DelegationViewModel>>();
            await delegationView.InitializeAsync(ct);
            await delegationView.SendToUserAsync("user_id", _userId, ct);

            // 解析参数获取 agentName 和 task
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

        // 普通工具 → ToolExecutionCardView
        if (!_toolExecutions.TryGetValue(evt.CallId, out var toolCard))
        {
            toolCard = (ToolExecutionCardView)_sp
                .GetRequiredService<CardView<LlmToolExecutionViewModel>>();
            await toolCard.InitializeAsync(ct);
            await toolCard.SendToUserAsync("user_id", _userId, ct);
            _toolExecutions[evt.CallId] = toolCard;
        }

        var (purpose, cleanArgs) = ExtractPurpose(evt.ArgumentsJson);
        await toolCard.UpdateForToolStartAsync(evt.ToolName ?? "未知工具", cleanArgs, purpose, ct);
    }

    private async Task OnAfterToolExecute(AfterToolExecuteEvent evt, CancellationToken ct)
    {
        // DelegateToAgent 完成 → 释放子 Agent 状态
        if (evt.ToolName == "DelegateToAgent" && _activeDelegationCard is not null)
        {
            await _activeDelegationCard.UpdateForCompletedAsync(ct);
            DisposeChildSubs();
            _activeDelegationCard = null;
            return;
        }

        // 普通工具结果
        if (!_toolExecutions.TryGetValue(evt.CallId, out var toolCard)) return;

        var resultText = evt.Error ?? evt.ResultJson ?? "";
        if (resultText.Length > 500)
            resultText = string.Concat(resultText.AsSpan(0, 500), "\n...");

        await toolCard.UpdateForToolResultAsync(
            string.IsNullOrWhiteSpace(resultText) ? "无返回结果" : resultText,
            isError: evt.Error is not null,
            ct);
    }

    #region 子 Agent 事件

    private async Task OnSubAgentStarted(SubAgentStartedEvent evt, CancellationToken ct)
    {
        // 订阅子 Agent 的事件（以子 Agent 的 AgentId 为 key）
        _childSubs.Add(_bus.Subscribe<ModelContentEvent>(evt.SubAgentId, OnChildModelContent));
        _childSubs.Add(_bus.Subscribe<BeforeToolExecuteEvent>(evt.SubAgentId, OnChildBeforeToolExecute));
        _childSubs.Add(_bus.Subscribe<AfterToolExecuteEvent>(evt.SubAgentId, OnChildAfterToolExecute));
    }

    private async Task OnSubAgentCompleted(SubAgentCompletedEvent evt, CancellationToken ct)
    {
        // 最终 FlushAsync（将累积的文本一次性刷到卡片）
        if (_activeDelegationCard is not null)
            await _activeDelegationCard.UpdateForCompletedAsync(ct);

        DisposeChildSubs();
        _activeDelegationCard = null;
    }

    private async Task OnChildModelContent(ModelContentEvent evt, CancellationToken ct)
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
                // 子 Agent 文本输出结束，FlushAsync 累积的文本
                await _activeDelegationCard.FlushAsync(ct);
                break;
        }
    }

    private async Task OnChildBeforeToolExecute(BeforeToolExecuteEvent evt, CancellationToken ct)
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

    private async Task OnChildAfterToolExecute(AfterToolExecuteEvent evt, CancellationToken ct)
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

    #endregion

    /// <summary>
    /// 从工具参数 JSON 中提取 purpose 字段（工具调用意图），并将其从剩余参数中移除避免重复展示。
    /// </summary>
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
        DisposeChildSubs();
        foreach (var sub in _subs) sub.Dispose();
    }
}
