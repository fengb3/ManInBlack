using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 将模型流式输出通过 EventBus 广播为模型内容事件（<see cref="ModelContentEvent"/>）
/// 与工具调用参数增量事件（<see cref="ToolCallStreamEvent"/>）。
/// </summary>
[ServiceRegister.Scoped]
public class EventPublishingMiddleware(EventBus eventBus) : AgentMiddleware
{
    /// <summary>
    /// 按 CallId 累积已见的参数 JSON，用于计算本次增量。
    /// 中间件为 Scoped，生命周期与一次 Agent 请求一致。
    /// </summary>
    private readonly Dictionary<string, string> _accumulatedArgsByCallId = [];

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var key = context.AgentId;

        await foreach (var update in next().WithCancellation(ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        await eventBus.PublishAsync(key, new ModelContentEvent
                        {
                            AgentId = key, Kind = ModelContentKind.Text, Text = text.Text
                        }, ct);
                        break;

                    case TextReasoningContent reasoning:
                        await eventBus.PublishAsync(key, new ModelContentEvent
                        {
                            AgentId = key, Kind = ModelContentKind.Reasoning, Text = reasoning.Text
                        }, ct);
                        break;

                    case UsageContent usage:
                        await eventBus.PublishAsync(key, new ModelContentEvent
                        {
                            AgentId = key, Kind = ModelContentKind.Usage, Usage = usage.Details
                        }, ct);
                        break;

                    case FunctionCallContent fcc when IsArgumentDelta(fcc):
                        await PublishToolCallStreamEventAsync(key, fcc, ct);
                        break;
                }
            }

            yield return update;
        }

        await eventBus.PublishAsync(key, new ModelContentEvent
        {
            AgentId = key, Kind = ModelContentKind.Completed
        }, ct);
    }

    /// <summary>
    /// 判断 <see cref="FunctionCallContent"/> 是否为参数增量分片。
    /// </summary>
    private static bool IsArgumentDelta(FunctionCallContent fcc)
    {
        return fcc.AdditionalProperties is not null
               && fcc.AdditionalProperties.TryGetValue(ToolCallStreamEventKeys.IsArgumentDelta, out var value)
               && value is true;
    }

    /// <summary>
    /// 发布 <see cref="ToolCallStreamEvent"/>。
    /// 累积参数按 CallId 维护；增量 = 新累积串去掉上次累积串前缀。
    /// </summary>
    private async Task PublishToolCallStreamEventAsync(
        string key,
        FunctionCallContent fcc,
        CancellationToken ct)
    {
        var callId = fcc.CallId ?? string.Empty;
        var accumulated = fcc.AdditionalProperties is not null
                          && fcc.AdditionalProperties.TryGetValue(ToolCallStreamEventKeys.AccumulatedArgumentsJson, out var raw)
                          && raw is string rawStr
            ? rawStr
            : string.Empty;

        var previous = _accumulatedArgsByCallId.GetValueOrDefault(callId) ?? string.Empty;
        var delta = accumulated.StartsWith(previous, StringComparison.Ordinal)
            ? accumulated[previous.Length..]
            : accumulated;

        _accumulatedArgsByCallId[callId] = accumulated;

        await eventBus.PublishAsync(key, new ToolCallStreamEvent
        {
            AgentId = key,
            CallId = callId,
            ToolName = fcc.Name,
            ArgumentDelta = delta,
            AccumulatedArguments = accumulated
        }, ct);
    }
}
