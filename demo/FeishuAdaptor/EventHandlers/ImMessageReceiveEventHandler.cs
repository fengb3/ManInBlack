using System.Text.Json;
using FeishuAdaptor.Helper;
using FeishuAdaptor.Middlewares;
using FeishuNetSdk;
using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using ManInBlack.AI;
using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;

namespace FeishuAdaptor.EventHandlers;

public class ImMessageReceiveEventHandler(
    ILogger<EventHandler> logger,
    AgentLauncher agentLauncher,
    IServiceProvider sp
)
    : IEventHandler<EventV2Dto<ImMessageReceiveV1EventBodyDto>, ImMessageReceiveV1EventBodyDto>
{
    public Task ExecuteAsync(
        EventV2Dto<ImMessageReceiveV1EventBodyDto> input,
        CancellationToken cancellationToken = new()
    )
    {
        _ = Task.Run(async () => await agentLauncher.LaunchAsync(input));
        return Task.CompletedTask;
    }


}

[ServiceRegister.Singleton]
public class AgentLauncher(IServiceProvider rootServiceProvider, ILogger<AgentLauncher> logger)
{
    public async Task LaunchAsync(
        EventV2Dto<ImMessageReceiveV1EventBodyDto> input
    )
    {
        using var scope = rootServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        
        logger.LogInformation(
            "Received message from user {userId}: {content}",
            input.Event.Sender.SenderId.OpenId,
            input.Event.Message?.Content
        );

        var pipeline = new AgentPipelineBuilder()
            .Use<FeishuCardMiddleware>()
            .UseDefault()
            .Build(sp);

        var agentContext = sp.GetRequiredService<AgentContext>();

        agentContext.AgentId    = Guid.NewGuid().ToString();
        agentContext.ParentId   = input.Event!.Sender!.SenderId!.OpenId!;
        agentContext.ParentType = "FeishuUser";
        var userLlmInput = await HandleMessage(sp, input);

        agentContext.UserInput = userLlmInput;

        var updates = pipeline(agentContext);

        try
        {
            await foreach (var _ in updates) {}
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error processing message from user {userId}", input.Event.Sender.SenderId.OpenId);
        }
    }

    private async Task<string> HandleMessage(IServiceProvider sp,EventV2Dto<ImMessageReceiveV1EventBodyDto> input, CancellationToken ct = default)
    {
        var userId         = input.Event.Sender.SenderId.OpenId;
        var messageType    = input.Event.Message.MessageType;
        var messageContent = input.Event.Message.Content;

        var result = "";

        switch (messageType)
        {
            // Handle file uploads — download and save to user workspace
            case "file":
                try
                {
                    var tenantApi     = sp.GetRequiredService<IFeishuTenantApi>();
                    var userWorkspace = sp.GetRequiredService<IUserWorkspace>();
                
                    var doc       = JsonDocument.Parse(messageContent);
                    var fileKey   = doc.RootElement.GetProperty("file_key").GetString()!;
                    var fileName  = doc.RootElement.GetProperty("file_name").GetString()!;
                    var messageId = input.Event.Message.MessageId;

                    var savePath       = Path.Combine(userWorkspace.WorkingDirectory, fileName);

                    using var response =
                        await tenantApi.GetImV1MessagesByMessageIdResourcesByFileKeyAsync(
                            messageId,
                            fileKey,
                            "file",
                            ct
                        );
                    response.EnsureSuccessStatusCode();

                    await using var stream     = await response.Content.ReadAsStreamAsync(ct);
                    await using var fileStream = File.Create(savePath);
                    await stream.CopyToAsync(fileStream, ct);

                    result =
                        "["
                        + $"User has send you a file: {fileName} — saved to user workspace. "
                        // + $"The file can be read using the ReadFile tool with path: {fileName} "
                        // + "if u don't know why they upload this file"
                        + "don't read the file before asking user why they upload this file."
                        + "]";

                    logger.LogInformation(
                        "Downloaded file {fileName} for user {userId}",
                        fileName,
                        userId
                    );
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to download file for user {userId}", userId);
                    result = "[User uploaded a file but the download failed.]";
                }
                break;
            // Handle text messages
            case "text":
            {
                var doc  = JsonDocument.Parse(messageContent);
                var text = doc.RootElement.GetProperty("text").GetString()!;
                result = text;
                break;
            }
        }

        return result;
    }
}