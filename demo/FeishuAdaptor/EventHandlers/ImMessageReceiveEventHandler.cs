using System.Text.Json;
using FeishuAdaptor.FeishuCard.CardViews;
using FeishuNetSdk;
using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Events;
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

            var subs = new List<IDisposable>();

            var updates = factory.RunAsync(
                "feishu-agent",
                userLlmInput,
                userId,
                "feishu_user",
                ctx =>
                {
                    ctx.SystemPrompt += $"""
                        <system>
                        你的面对的用户的 飞书 open id 是: {openId}
                        </system>
                        """;

                    var key = ctx.AgentId;
                    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
                    var sp = ctx.ServiceProvider;

                    // 状态跟踪
                    string lastLlmType = "";
                    LlmOutputViewModel? lastOutput = null;
                    LlmReasoningViewModel? lastReasoning = null;
                    List<CardViewBase> streamingCardViews = [];
                    var toolExecutions = new Dictionary<string, ToolExecutionCardView>();

                    subs.Add(bus.Subscribe<ModelContentEvent>(key, async (evt, ct) =>
                    {
                        switch (evt.Kind)
                        {
                            case ModelContentKind.Reasoning:
                            {
                                if (string.IsNullOrEmpty(evt.Text)) break;
                                if (lastLlmType != nameof(LlmReasoningViewModel))
                                {
                                    var (vm1, view1) = CreateCard<LlmReasoningViewModel>(sp, openId);
                                    streamingCardViews.Add(view1);
                                    lastReasoning = vm1;
                                    lastLlmType = nameof(LlmReasoningViewModel);
                                }
                                lastReasoning!.Reasoning += evt.Text;
                                break;
                            }
                            case ModelContentKind.Text:
                            {
                                if (string.IsNullOrEmpty(evt.Text)) break;
                                if (lastLlmType != nameof(LlmOutputViewModel))
                                {
                                    var (vm2, view2) = CreateCard<LlmOutputViewModel>(sp, openId);
                                    streamingCardViews.Add(view2);
                                    lastOutput = vm2;
                                    lastLlmType = nameof(LlmOutputViewModel);
                                }
                                lastOutput!.Output += evt.Text;
                                break;
                            }
                            case ModelContentKind.Completed:
                            {
                                foreach (var view in streamingCardViews)
                                {
                                    try { await view.CloseStreamingAsync(ct); }
                                    catch { }
                                }
                                break;
                            }
                        }
                    }));

                    subs.Add(bus.Subscribe<BeforeToolExecuteEvent>(key, async (evt, ct) =>
                    {
                        lastLlmType = "";

                        if (!toolExecutions.TryGetValue(evt.CallId, out var toolCard))
                        {
                            toolCard = (ToolExecutionCardView)sp
                                .GetRequiredService<CardView<LlmToolExecutionViewModel>>();
                            await toolCard.InitializeAsync(ct);
                            await toolCard.SendToUserAsync("open_id", openId, ct);
                            toolExecutions[evt.CallId] = toolCard;
                        }

                        var toolName = evt.ToolName ?? "未知工具";
                        var description = "";

                        // 从 ArgumentsJson 提取描述（如 RunBash 的注释行）
                        if (toolName == "RunBash" && evt.ArgumentsJson is not null)
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(evt.ArgumentsJson);
                                if (doc.RootElement.TryGetProperty("command", out var cmdProp))
                                {
                                    var cmdStr = cmdProp.GetString() ?? "";
                                    var firstLine = cmdStr.TrimStart().Split('\n')[0].Trim();
                                    if (firstLine.StartsWith("#"))
                                        description = firstLine.TrimStart('#', ' ').Trim();
                                }
                            }
                            catch { }
                        }

                        var arguments = evt.ArgumentsJson ?? "无参数";
                        await toolCard.UpdateForToolStartAsync(toolName, arguments, description, ct);
                    }));

                    subs.Add(bus.Subscribe<AfterToolExecuteEvent>(key, async (evt, ct) =>
                    {
                        if (!toolExecutions.TryGetValue(evt.CallId, out var toolCard)) return;

                        var resultText = evt.ResultJson ?? "";
                        if (resultText.Length > 500)
                            resultText = string.Concat(resultText.AsSpan(0, 500), "\n...");

                        await toolCard.UpdateForToolResultAsync(
                            string.IsNullOrWhiteSpace(resultText) ? "无返回结果" : resultText,
                            isError: evt.Error is not null,
                            ct);
                    }));
                });

            await foreach (var _ in updates) { }

            foreach (var sub in subs) sub.Dispose();
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

    private static (T ViewModel, CardView<T> View) CreateCard<T>(IServiceProvider sp, string openId) where T : ViewModelBase
    {
        var view = sp.GetRequiredService<CardView<T>>();
        view.InitializeAsync().GetAwaiter().GetResult();
        view.SendToUserAsync("open_id", openId).GetAwaiter().GetResult();
        return (view.ViewModel, view);
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
