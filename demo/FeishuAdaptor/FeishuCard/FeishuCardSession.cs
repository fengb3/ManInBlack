using System.Text.Json;
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

        if (!_toolExecutions.TryGetValue(evt.CallId, out var toolCard))
        {
            toolCard = (ToolExecutionCardView)_sp
                .GetRequiredService<CardView<LlmToolExecutionViewModel>>();
            await toolCard.InitializeAsync(ct);
            await toolCard.SendToUserAsync("user_id", _userId, ct);
            _toolExecutions[evt.CallId] = toolCard;
        }

        var toolName = evt.ToolName ?? "未知工具";
        var description = ExtractToolDescription(toolName, evt.ArgumentsJson);
        var arguments = evt.ArgumentsJson ?? "无参数";
        await toolCard.UpdateForToolStartAsync(toolName, arguments, description, ct);
    }

    private async Task OnAfterToolExecute(AfterToolExecuteEvent evt, CancellationToken ct)
    {
        if (!_toolExecutions.TryGetValue(evt.CallId, out var toolCard)) return;

        var resultText = evt.ResultJson ?? "";
        if (resultText.Length > 500)
            resultText = string.Concat(resultText.AsSpan(0, 500), "\n...");

        await toolCard.UpdateForToolResultAsync(
            string.IsNullOrWhiteSpace(resultText) ? "无返回结果" : resultText,
            isError: evt.Error is not null,
            ct);
    }

    private static string ExtractToolDescription(string toolName, string? argumentsJson)
    {
        if (toolName != "RunBash" || argumentsJson is null) return "";

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("command", out var cmdProp))
            {
                var cmdStr = cmdProp.GetString() ?? "";
                var firstLine = cmdStr.TrimStart().Split('\n')[0].Trim();
                if (firstLine.StartsWith("#"))
                    return firstLine.TrimStart('#', ' ').Trim();
            }
        }
        catch { }

        return "";
    }

    private (T ViewModel, CardView<T> View) CreateCard<T>() where T : ViewModelBase
    {
        var view = _sp.GetRequiredService<CardView<T>>();
        view.InitializeAsync().GetAwaiter().GetResult();
        view.SendToUserAsync("user_id", _userId).GetAwaiter().GetResult();
        return (view.ViewModel, view);
    }

    public void Dispose()
    {
        foreach (var sub in _subs) sub.Dispose();
    }
}
