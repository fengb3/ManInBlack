using System.Text.Json;
using FeishuAdaptor.Helper;
using FeishuAdaptor.Middlewares;
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
    public Task ExecuteAsync(EventV2Dto<ImMessageReceiveV1EventBodyDto> input,
        CancellationToken cancellationToken = new())
    {
        _ = ExecuteInner(input)
            .ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        logger.LogError(
                            t.Exception,
                            "Error processing message from user {userId}",
                            input.Event?.Sender?.SenderId?.OpenId!
                        );
                    }
                },
                cancellationToken
            );

        return Task.CompletedTask;
    }

    private async Task ExecuteInner(EventV2Dto<ImMessageReceiveV1EventBodyDto> input)
    {
        using var scope = sp.CreateScope();

        var cts = new CancellationTokenSource();
        var ct = cts.Token;

        var pipeline = new AgentPipelineBuilder()
            .Use<FeishuCardMiddleware>()
            .Use<FeishuToolCardMiddleware>()
            .UseDefault()
            .Build(sp);

        var agentContext = scope.ServiceProvider.GetRequiredService<AgentContext>();

        agentContext.AgentId = Guid.NewGuid().ToString();
        agentContext.ParentId = input.Event!.Sender!.SenderId!.OpenId!;
        agentContext.ParentType = "FeishuUser";
        
        var userInputContent = input.Event.Message?.Content;
        
        // load it into JSON
        var userInputContentJson = JsonElement.Parse(userInputContent!);

        if (!userInputContentJson.TryGetProperty("text", out var textJson))
        {
            // 不支持的消息类型
            logger.LogWarning(
                "Unsupported message type from user {userId}: {content}",
                input.Event.Sender.SenderId.OpenId,
                userInputContent
            );
            return;
        }

        agentContext.UserInput = textJson.GetString() ?? "";

        var updates = pipeline(agentContext);
        await foreach (var _ in updates.WithCancellation(ct))
        {
        }
    }
}