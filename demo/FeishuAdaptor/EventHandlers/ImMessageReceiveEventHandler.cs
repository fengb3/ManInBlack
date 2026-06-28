using System.Text.Json;
using FeishuAdaptor.FeishuCard;
using FeishuNetSdk;
using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Services;
using Microsoft.Extensions.DependencyInjection;

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
            return Task.CompletedTask;

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
        var userId = input.Event!.Sender!.SenderId!.UserId!;

        var cts = factory.RegisterAndCancelExisting(userId);

        logger.LogInformation(
            "Received message from user {userId}: {content}",
            userId,
            input.Event.Message?.Content
        );

        try
        {
            string userLlmInput;
            using (var messageScope = rootServiceProvider.CreateScope())
            {
                userLlmInput = await HandleMessage(messageScope.ServiceProvider, input, cts.Token);
            }

            FeishuCardSession? cardSession = null;

            var updates = factory.RunAsync(
                "feishu-agent",
                userLlmInput,
                userId,
                "feishu_user",
                ctx =>
                {
                    ctx.SystemPrompt += $"""
                        <system>
                        你的面对的用户的 飞书 user id 是: {userId}
                        </system>
                        """;

                    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
                    cardSession = new FeishuCardSession(ctx.ServiceProvider, userId, bus, ctx.AgentId);
                    cardSession.Subscribe();
                });

            await foreach (var _ in updates) { }

            cardSession?.Dispose();
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
                input.Event.Sender.SenderId.UserId
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

    /// <summary>
    /// 在 Agent 运行之前的独立 scope 中,解析指定用户的工作空间目录。
    /// </summary>
    /// <remarks>
    /// 文件下载发生在 Agent 运行之前,此时 scope 内的 <see cref="AgentContext"/> 尚未被
    /// AgentFactory 填充。<see cref="IUserWorkspace"/> 的实现(FileUserWorkspace)依据
    /// <see cref="AgentContext.RootUserId"/> 决定目录,因此这里必须先写入真实发送者,
    /// 否则 RootUserId/ParentId 为空,所有用户的文件都会落到「空字符串用户」的工作空间。
    /// </remarks>
    internal static string ResolveWorkspaceDirectory(IServiceProvider sp, string userId)
    {
        var agentContext = sp.GetRequiredService<AgentContext>();
        agentContext.RootUserId = userId;
        agentContext.ParentId = userId;
        agentContext.ParentType = "feishu_user";

        return sp.GetRequiredService<IUserWorkspace>().WorkingDirectory;
    }

    private async Task<string> HandleMessage(
        IServiceProvider sp,
        EventV2Dto<ImMessageReceiveV1EventBodyDto> input,
        CancellationToken ct = default
    )
    {
        var userId = input.Event!.Sender!.SenderId!.UserId!;
        var messageType = input.Event!.Message!.MessageType!;
        var messageContent = input.Event!.Message!.Content!;

        var result = "";

        switch (messageType)
        {
            case "file":
            {
                try
                {
                    var tenantApi = sp.GetRequiredService<IFeishuTenantApi>();

                    var doc = JsonDocument.Parse(messageContent);
                    var fileKey = doc.RootElement.GetProperty("file_key").GetString()!;
                    var fileName = doc.RootElement.GetProperty("file_name").GetString()!;
                    var messageId = input.Event!.Message!.MessageId!;

                    // 文件落到「当前发送者」的工作空间,而非空字符串用户
                    var savePath = Path.Combine(ResolveWorkspaceDirectory(sp, userId), fileName);

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
