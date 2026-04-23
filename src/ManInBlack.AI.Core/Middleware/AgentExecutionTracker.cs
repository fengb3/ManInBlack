using System.Collections.Concurrent;
using ManInBlack.AI.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Core.Middleware;

/// <summary>
/// 跟踪每个用户正在运行的 Agent，支持在新消息到来时取消旧 Agent
/// </summary>
public class AgentExecutionTracker(ILogger<AgentExecutionTracker> logger)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tracking = new();

    /// <summary>
    /// 取消该用户现有的 Agent（如果有），注册并返回新的 CancellationTokenSource
    /// </summary>
    public CancellationTokenSource RegisterAndCancelExisting(string userId)
    {
        var newCts = new CancellationTokenSource();

        _tracking.AddOrUpdate(
            userId,
            _ => newCts,
            (_, existingCts) =>
            {
                logger.LogInformation("取消用户 {UserId} 的正在运行的 Agent", userId);
                existingCts.Cancel();
                return newCts;
            }
        );

        return newCts;
    }

    /// <summary>
    /// 释放该用户的跟踪记录。仅当字典中存储的 CTS 与传入的是同一实例时才移除，防止误删新 Agent 的 CTS
    /// </summary>
    public void Release(string userId, CancellationTokenSource cts)
    {
        _tracking.TryRemove(new KeyValuePair<string, CancellationTokenSource>(userId, cts));
    }
}
