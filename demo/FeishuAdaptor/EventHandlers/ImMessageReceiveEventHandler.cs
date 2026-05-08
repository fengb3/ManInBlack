using System.Text.Json;
using FeishuNetSdk;
using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;

namespace FeishuAdaptor.EventHandlers;

public partial class ImMessageReceiveEventHandler(
    AgentLauncher agentLauncher,
    ILogger<ImMessageReceiveEventHandler> logger
) : IEventHandler<EventV2Dto<ImMessageReceiveV1EventBodyDto>, ImMessageReceiveV1EventBodyDto>
{
    public Task ExecuteAsync(
        EventV2Dto<ImMessageReceiveV1EventBodyDto> input,
        CancellationToken cancellationToken = new()
    )
    {
        if (input.Event?.Message?.ChatType != "p2p")
            return Task.CompletedTask; // Only handle 1-on-1 messages for now

        LogMessageReceived(logger, input.EventId, input.Event.Message.MessageType);

        _ = Task.Run(async () => await agentLauncher.LaunchAsync(input))
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    logger.LogError(t.Exception, "error when launch agent");
                }
            });
        return Task.CompletedTask;
    }

    [LoggerMessage(
        LogLevel.Information,
        "Received ImMessageReceive event: {eventId}, message type: {messageType}"
    )]
    static partial void LogMessageReceived(
        ILogger<ImMessageReceiveEventHandler> logger,
        string eventId,
        string messageType
    );
}

[ServiceRegister.Singleton]
public class AgentLauncher(
    IServiceProvider rootServiceProvider,
    AgentFactory factory,
    ILogger<AgentLauncher> logger
)
{
    public async Task LaunchAsync(EventV2Dto<ImMessageReceiveV1EventBodyDto> input)
    {
        var userId = input.Event!.Sender!.SenderId!.UserId!; // user id is unique over all application, can be used as parent id for agent context

        // 取消该用户正在运行的旧 Agent，注册新的 CancellationTokenSource
        var cts = factory.RegisterAndCancelExisting(userId);

        logger.LogInformation(
            "Received message from user {userId}: {content}",
            userId,
            input.Event.Message?.Content
        );

        try
        {
            // 处理消息内容（仍需 scope 解析飞书 API）
            string userLlmInput;
            using (var messageScope = rootServiceProvider.CreateScope())
            {
                userLlmInput = await HandleMessage(messageScope.ServiceProvider, input, cts.Token);
            }

            // 使用 Factory 运行 agent，通过 configure 回调注入动态 SystemPrompt
            var openId = input.Event!.Sender!.SenderId!.OpenId!;
            var updates = factory.RunAsync(
                "feishu-agent",
                userLlmInput,
                userId,
                "feishu_user",
                ctx => ctx.SystemPrompt += $"""
                    <system>
                    你的面对的用户的 飞书 open id 是: {openId}
                    </system>
                    """,
                cts.Token);

            await foreach (var _ in updates) { }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Agent 被取消，用户 {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error processing message from user {userId}",
                input.Event.Sender.SenderId.OpenId
            );
            throw;
        }
        finally
        {
            factory.Release(userId, cts);
            logger.LogInformation(
                "Finished processing message from user {userId}",
                userId
            );
        }
    }

    private async Task<string> HandleMessage(
        IServiceProvider sp,
        EventV2Dto<ImMessageReceiveV1EventBodyDto> input,
        CancellationToken ct = default
    )
    {
        var userId = input.Event!.Sender!.SenderId!.OpenId!;
        var messageType = input.Event!.Message!.MessageType!;
        var messageContent = input.Event!.Message!.Content!;

        var result = "";

        switch (messageType)
        {
            // Handle file uploads — download and save to user workspace
            case "file":
            {
                try
                {
                    var tenantApi = sp.GetRequiredService<IFeishuTenantApi>();
                    var userWorkspace = sp.GetRequiredService<IUserWorkspace>();

                    var doc = JsonDocument.Parse(messageContent);
                    var fileKey = doc.RootElement.GetProperty("file_key").GetString()!;
                    var fileName = doc.RootElement.GetProperty("file_name").GetString()!;
                    var messageId = input.Event!.Message!.MessageId!;

                    var savePath = Path.Combine(userWorkspace.WorkingDirectory, fileName);

                    using var response =
                        await tenantApi.GetImV1MessagesByMessageIdResourcesByFileKeyAsync(
                            messageId,
                            fileKey,
                            "file",
                            ct
                        );
                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    await using var fileStream = File.Create(savePath);
                    await stream.CopyToAsync(fileStream, ct);

                    result =
                        "["
                        + $"User has send you a file: {fileName} — saved to your workspace. "
                        // + $"The file can be read using the ReadFile tool with path: {fileName} "
                        // + "if u don't know why they upload this file"
                        + "don't read the file before you know user why they upload this file."
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
            }
            // Handle text messages
            case "text":
            {
                var doc = JsonDocument.Parse(messageContent);
                var text = doc.RootElement.GetProperty("text").GetString()!;
                result = text;
                break;
            }
            default:
            {
                logger.LogWarning(
                    "Received unsupported message type {messageType} from user {userId}",
                    messageType,
                    userId
                );
                result = $"[Received unsupported message type: {messageType}, content: {messageContent}]";
                break;
            }
        }

        return result;
    }
}
