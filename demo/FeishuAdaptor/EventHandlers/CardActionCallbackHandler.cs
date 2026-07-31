using FeishuAdaptor.Tools;
using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Services;
using Microsoft.Extensions.Logging;

namespace FeishuAdaptor.EventHandlers;

/// <summary>
/// 飞书卡片回传交互处理器：用户点击 AskUser 卡片的按钮/提交时触发，
/// 按 requestId 在 <see cref="PendingAskRegistry"/> 中解决挂起的提问。
/// 由 <c>AddFeishuNetSdk(...)</c> 自动发现（同 <see cref="ImMessageReceiveEventHandler"/>）。
/// </summary>
public class CardActionCallbackHandler(
    PendingAskRegistry registry,
    ILogger<CardActionCallbackHandler> logger)
    : ICallbackHandler<CallbackV2Dto<CardActionTriggerEventBodyDto>, CardActionTriggerEventBodyDto, CardActionTriggerResponseDto>
{
    // 注意：卡片回传交互（card-callback）走 CallbackV2Dto<T>（实现 IAmCallbackDto），
    // 而非普通事件流用的 EventV2Dto<T>——这是 ICallbackHandler 的 T1 约束所要求。
    public Task<CardActionTriggerResponseDto> ExecuteAsync(
        CallbackV2Dto<CardActionTriggerEventBodyDto> input,
        CancellationToken cancellationToken)
    {
        var body = input.Event;
        var action = body?.Action;
        var value = action?.Value;

        if (value is null || !value.TryGetValue("requestId", out var ridObj) || ridObj is not string requestId)
        {
            logger.LogDebug("收到无 requestId 的卡片回调，忽略");
            return Task.FromResult(Toast("无效的提问回调"));
        }

        if (!registry.TryGet(requestId, out var ask) || ask is null)
        {
            logger.LogDebug("卡片回调 requestId={RequestId} 无对应挂起提问（已过期/已回答）", requestId);
            return Task.FromResult(Toast("问题已过期或已回答"));
        }

        var operatorUserId = body!.Operator?.UserId;
        if (!string.IsNullOrEmpty(operatorUserId)
            && !string.Equals(operatorUserId, ask.AskedUserId, StringComparison.Ordinal))
        {
            logger.LogDebug("卡片回调 requestId={RequestId} 的操作者 {Operator} 与提问对象 {Asked} 不一致，忽略", requestId, operatorUserId, ask.AskedUserId);
            return Task.FromResult(Toast("无权回答此问题"));
        }

        var selected = CollectSelected(action!, ask.MultiSelect);
        registry.Resolve(requestId, new AskUserResult(selected));
        return Task.FromResult(Toast("已收到你的选择"));
    }

    private static string[] CollectSelected(CardActionTriggerEventBodyDto.ActionSuffix action, bool multiSelect)
    {
        if (multiSelect)
        {
            if (action.FormValue is not null && action.FormValue.TryGetValue("opts", out var opts))
                return ToStringArray(opts);
            return action.Options ?? Array.Empty<string>();
        }

        if (action.Value is not null && action.Value.TryGetValue("option", out var opt))
            return new[] { opt?.ToString() ?? string.Empty };
        return Array.Empty<string>();
    }

    private static string[] ToStringArray(object? opts) => opts switch
    {
        null => Array.Empty<string>(),
        string[] arr => arr,
        IEnumerable<object> list => list.Select(o => o?.ToString() ?? string.Empty).ToArray(),
        System.Text.Json.JsonElement je => je.ValueKind == System.Text.Json.JsonValueKind.Array
            ? je.EnumerateArray().Select(e => e.ToString()).ToArray()
            : new[] { je.ToString() },
        _ => new[] { opts.ToString() ?? string.Empty },
    };

    private static CardActionTriggerResponseDto Toast(string msg) => new()
    {
        // ToastSuffix.Content 为直接字符串属性；Type(ToastType?) 可选，省略。
        Toast = new CardActionTriggerResponseDto.ToastSuffix { Content = msg },
    };
}
