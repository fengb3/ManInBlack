using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 将模型流式输出通过 EventBus 广播为 ModelContentEvent
/// </summary>
[ServiceRegister.Scoped]
public class EventPublishingMiddleware(EventBus eventBus) : AgentMiddleware
{
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
                var evt = content switch
                {
                    TextContent text => new ModelContentEvent
                    {
                        AgentId = key, Kind = ModelContentKind.Text, Text = text.Text
                    },
                    TextReasoningContent reasoning => new ModelContentEvent
                    {
                        AgentId = key, Kind = ModelContentKind.Reasoning, Text = reasoning.Text
                    },
                    UsageContent usage => new ModelContentEvent
                    {
                        AgentId = key, Kind = ModelContentKind.Usage, Usage = usage.Details
                    },
                    _ => null
                };

                if (evt is not null)
                    await eventBus.PublishAsync(key, evt, ct);
            }

            yield return update;
        }

        await eventBus.PublishAsync(key, new ModelContentEvent
        {
            AgentId = key, Kind = ModelContentKind.Completed
        }, ct);
    }
}
