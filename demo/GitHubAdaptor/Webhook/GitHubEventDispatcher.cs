using GitHubAdaptor.Handlers;
using GitHubAdaptor.Models;
using ManInBlack.AI.Abstraction.Attributes;
using Microsoft.Extensions.Logging;

namespace GitHubAdaptor.Webhook;

[ServiceRegister.Singleton]
public class GitHubEventDispatcher(
    PullRequestHandler handler,
    ILogger<GitHubEventDispatcher> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task DispatchAsync(string eventType, string body, CancellationToken ct = default)
    {
        logger.LogInformation("收到 GitHub 事件: {EventType}", eventType);

        if (eventType != "pull_request")
        {
            logger.LogDebug("忽略非 PR 事件: {EventType}", eventType);
            return;
        }

        var payload = JsonSerializer.Deserialize<PullRequestPayload>(body, JsonOptions);
        if (payload is null)
        {
            logger.LogError("无法解析 pull_request payload");
            return;
        }

        if (payload.Action is not ("opened" or "synchronize"))
        {
            logger.LogDebug("忽略 PR action: {Action}", payload.Action);
            return;
        }

        logger.LogInformation("处理 PR #{Number} ({Action}) on {Repo}",
            payload.Number, payload.Action, payload.Repository?.FullName);

        await handler.HandleAsync(payload, ct);
    }
}
