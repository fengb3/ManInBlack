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
        var openId = input.Event!.Sender!.SenderId!.OpenId!;

        var cts = factory.RegisterAndCancelExisting(userId);

        logger.LogInformation(
            "Received message from user {userId} (open id: {openId}): {content}",
            userId,
            openId,
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
                    你的面对的用户的飞书 
                    user id: {userId}
                    open id: {openId}
                    </system>
                    """;

                    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
                    cardSession = new FeishuCardSession(
                        ctx.ServiceProvider,
                        userId,
                        bus,
                        ctx.AgentId
                    );
                    cardSession.Subscribe();
                }
            );

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
            logger.LogInformation("Finished processing message from user {userId}", userId);
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

    /// <summary>
    /// 构造「用户上传文件」后发给 agent 的提示词。
    /// </summary>
    internal static string BuildFileReceivedNotice(string fileName, string workspaceDir)
    {
        return $"用户上传了文件 {fileName} 已经保存在了你的工作路径 {workspaceDir}，"
            + "在你了解用户为何上传它之前，不要读取文件";
    }

    /// <summary>
    /// 把文本中的 @_user_N 占位符内联替换为被@者的可读信息:
    /// <c>@名字(open_id:.., user_id:.., union_id:..)</c>,只输出非空字段(<c>tenant_key</c> 不纳入)。
    /// mentions 为 null 时原样返回。对 p2p / group 均可复用。
    /// </summary>
    internal static string ResolveMentions(
        string text,
        IEnumerable<ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent>? mentions)
    {
        if (mentions is null)
            return text;

        // 按 key 长度降序替换:避免 @_user_1 误伤 @_user_10(消息含 10+ 个 @提及时)
        foreach (var mention in mentions
                     .Where(m => !string.IsNullOrEmpty(m.Key))
                     .OrderByDescending(m => m.Key.Length))
        {
            text = text.Replace(mention.Key, FormatMention(mention));
        }

        return text;
    }

    private static string FormatMention(ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent mention)
    {
        static bool Real(string? v) => !string.IsNullOrEmpty(v) && v != "all";

        var name = string.IsNullOrEmpty(mention.Name) ? "未知用户" : mention.Name;

        var id = mention.Id;
        var parts = new List<string>();
        if (Real(id?.OpenId)) parts.Add($"open_id:{id!.OpenId}");
        if (Real(id?.UserId)) parts.Add($"user_id:{id!.UserId}");
        if (Real(id?.UnionId)) parts.Add($"union_id:{id!.UnionId}");

        return parts.Count == 0 ? $"@{name}" : $"@{name}({string.Join(", ", parts)})";
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

                    var agentContext = sp.GetRequiredService<AgentContext>();
                    agentContext.RootUserId = userId;

                    var userWorkspace = sp.GetRequiredService<IUserWorkspace>();

                    var doc = JsonDocument.Parse(messageContent);
                    var fileKey = doc.RootElement.GetProperty("file_key").GetString()!;
                    var fileName = doc.RootElement.GetProperty("file_name").GetString()!;
                    var messageId = input.Event!.Message!.MessageId!;

                    // 文件落到「当前发送者」的工作空间,而非空字符串用户
                    var workspaceDir = ResolveWorkspaceDirectory(sp, userId);
                    var savePath = Path.Combine(workspaceDir, fileName);

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

                    result = BuildFileReceivedNotice(fileName, workspaceDir);

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
                result =
                    $"[Received unsupported message type: {messageType}, content: {messageContent}]";
                break;
            }
        }

        return result;
    }
}
