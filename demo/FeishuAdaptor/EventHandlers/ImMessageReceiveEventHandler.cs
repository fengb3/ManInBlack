using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using ManInBlack.AI;
using ManInBlack.AI.Core.Middleware;

namespace FeishuAdaptor.EventHandlers;

public class ImMessageReceiveEventHandler(
    ILogger<EventHandler> logger,
    IServiceProvider sp
) : IEventHandler<EventV2Dto<ImMessageReceiveV1EventBodyDto>, ImMessageReceiveV1EventBodyDto>
{
    public Task ExecuteAsync(EventV2Dto<ImMessageReceiveV1EventBodyDto> input, CancellationToken cancellationToken = new())
    {
        _ = ExecuteInner(input, cancellationToken)
            .ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        logger.LogError(
                            t.Exception,
                            "Error processing message from user {userId}",
                            input.Event.Sender.SenderId.OpenId
                        );
                    }
                },
                cancellationToken
            );
        
        return Task.CompletedTask;
    }

    private async Task ExecuteInner(EventV2Dto<ImMessageReceiveV1EventBodyDto> input, CancellationToken ct = new())
    {
        using var scope  = sp.CreateScope();
        var       logger = scope.ServiceProvider.GetRequiredService<ILogger<EventHandler>>();
        
        var pipeline     = new AgentPipelineBuilder().UseDefault().Build(sp);
        var agentContext = scope.ServiceProvider.GetRequiredService<AgentContext>();
        
        // set properties
        agentContext.AgentId    = Guid.NewGuid().ToString();
        agentContext.ParentId   = input.Event.Sender.SenderId.OpenId;
        agentContext.ParentType = "FeishuUser";
        agentContext.UserInput  = input.Event.Message.Content;
        
        var updates = pipeline(agentContext);

        await foreach (var update in updates)
        {
            logger.LogInformation(update.Contents.Select(c => c.ToString()).Aggregate((a, b) => a + b));
        }
    }
}