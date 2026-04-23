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
using ManInBlack.AI.Services.Abstraction;

namespace FeishuAdaptor.EventHandlers;

public class ImMessageReceiveEventHandler(
    AgentLauncher agentLauncher
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
public class AgentLauncher(
    IServiceProvider rootServiceProvider,
    AgentExecutionTracker executionTracker,
    ILogger<AgentLauncher> logger
)
{
    public async Task LaunchAsync(
        EventV2Dto<ImMessageReceiveV1EventBodyDto> input
    )
    {
        var userId = input.Event!.Sender!.SenderId!.OpenId!;

        // 取消该用户正在运行的旧 Agent，注册新的 CancellationTokenSource
        var cts = executionTracker.RegisterAndCancelExisting(userId);

        using var scope = rootServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        logger.LogInformation(
            "Received message from user {userId}: {content}",
            userId,
            input.Event.Message?.Content
        );

        var pipeline = new AgentPipelineBuilder()
            .Use<FeishuCardMiddleware>()
            .UseDefault()
            .Build(sp);

        // var sessionStorage = scope.ServiceProvider.GetRequiredService<ISessionStorage>();
        var userStorage = scope.ServiceProvider.GetRequiredService<IUserStorage>();

        var agentContext = sp.GetRequiredService<AgentContext>();
        agentContext.CancellationToken = cts.Token;

        var user = await userStorage.GetOrCreateUser(userId);

        agentContext.AgentId    = Guid.NewGuid().ToString();
        agentContext.ParentId   = userId;
        agentContext.ParentType = "feishu_user";
        agentContext.SessionId = await user.GetLatestSessionIdAsync(userStorage);
        
        agentContext.SystemPrompt +=
            $"""
            <system>
            你是运行在飞书中的智能 agent
            你的面对的用户的 飞书 open id 是: {input.Event!.Sender!.SenderId!.OpenId!}
            </system>
            """;
        
        var userLlmInput = await HandleMessage(sp, input);

        agentContext.UserInput = userLlmInput;

        var updates = pipeline(agentContext);

        try
        {
            await foreach (var _ in updates) { }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Agent 被取消，用户 {UserId}", userId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error processing message from user {userId}", userId);
        }
        finally
        {
            executionTracker.Release(userId, cts);
            logger.LogInformation(
                "Finished processing message from user {userId}",
                userId
            );

            await Task.Delay(1000); // Ensure all logs are flushed before the scope is disposed
        }
    }

    private async Task<string> HandleMessage(IServiceProvider sp,EventV2Dto<ImMessageReceiveV1EventBodyDto> input, CancellationToken ct = default)
    {
        var userId         = input.Event!.Sender!.SenderId!.OpenId!;
        var messageType    = input.Event!.Message!.MessageType!;
        var messageContent = input.Event!.Message!.Content!;

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
                    var messageId = input.Event!.Message!.MessageId!;

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