using System.Collections.Concurrent;
using System.Security.Cryptography;
using ManInBlack.AI.Abstraction.Storage;

namespace ManInBlack.AI.Storage;

/// <summary>
/// 内存版 <see cref="IUserStorage"/>，用于不安装 SQLite 持久化包时的默认行为。
/// 会话 ID 格式为 <c>{userId}_{unix秒}_{random}</c>，避免同秒碰撞。
/// </summary>
public sealed class InMemoryUserStorage : IUserStorage
{
    private readonly ConcurrentDictionary<string, UserEntry> _users = new();
    private readonly ConcurrentDictionary<string, List<SessionEntry>> _sessions = new();

    public Task<UserEntry> GetOrCreateUser(string userId)
    {
        var user = _users.GetOrAdd(userId, static id => new UserEntry
        {
            UserId = id,
            SelfHostUserId = id,
            CreatedAt = DateTime.UtcNow,
        });
        return Task.FromResult(user);
    }

    public Task SaveUserAsync(UserEntry userEntry)
    {
        _users[userEntry.UserId] = userEntry;
        return Task.CompletedTask;
    }

    public Task<string> CreateNewSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        var sessionId = GenerateSessionId(userId);
        var list = _sessions.GetOrAdd(userId, static _ => new List<SessionEntry>());
        lock (list)
        {
            list.Add(new SessionEntry(sessionId, source, DateTime.UtcNow));
        }
        return Task.FromResult(sessionId);
    }

    public Task<string?> GetLatestSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        if (!_sessions.TryGetValue(userId, out var list))
            return Task.FromResult<string?>(null);
        SessionEntry? latest;
        lock (list)
        {
            latest = list.Where(x => x.Source == source).MaxBy(x => x.LastAt);
        }
        return Task.FromResult(latest?.SessionId);
    }

    private static string GenerateSessionId(string userId)
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(3))[..6];
        return $"{userId}_{unixSeconds}_{random}";
    }

    private sealed record SessionEntry(string SessionId, SessionSource Source, DateTime LastAt);
}
