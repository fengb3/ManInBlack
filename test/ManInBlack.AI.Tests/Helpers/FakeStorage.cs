using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Storage;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tests.Helpers;

/// <summary>
/// 内存版 ISessionStorage，用 Dictionary 代替文件 I/O
/// </summary>
public class FakeSessionStorage : ISessionStorage
{
    private readonly Dictionary<string, List<ChatMessage>> _data = new();

    public Task SaveMessage(string sessionId, ChatMessage message)
    {
        if (!_data.TryGetValue(sessionId, out var list))
        {
            list = [];
            _data[sessionId] = list;
        }
        list.Add(message);
        return Task.CompletedTask;
    }

    public Task<IList<ChatMessage>> LoadMessages(string sessionId)
    {
        if (_data.TryGetValue(sessionId, out var list))
            return Task.FromResult<IList<ChatMessage>>([.. list]);
        return Task.FromResult<IList<ChatMessage>>([]);
    }

    /// <summary>
    /// 获取所有会话的消息，用于断言
    /// </summary>
    public IReadOnlyDictionary<string, List<ChatMessage>> AllData => _data;
}

/// <summary>
/// 内存版 IUserStorage
/// </summary>
public class FakeUserStorage : IUserStorage
{
    private readonly Dictionary<string, UserEntry> _users = new();

    public Task<UserEntry> GetOrCreateUser(string userId)
    {
        if (!_users.TryGetValue(userId, out var user))
        {
            user = new UserEntry { UserId = userId };
            _users[userId] = user;
        }
        return Task.FromResult(user);
    }

    public Task SaveUserAsync(UserEntry userEntry)
    {
        _users[userEntry.UserId] = userEntry;
        return Task.CompletedTask;
    }

    public async Task<string> CreateNewSessionIdAsync(string userId)
    {
        var user = await GetOrCreateUser(userId);
        var sessionId = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        user.SessionIds.Add(sessionId);
        await SaveUserAsync(user);
        return sessionId;
    }
}

/// <summary>
/// 内存版 IUserWorkspace
/// </summary>
public class FakeUserWorkspace : IUserWorkspace
{
    public string UserId { get; }
    public string AgentRoot { get; }
    public string UserRoot { get; }
    public string WorkingDirectory { get; set; }

    public FakeUserWorkspace(string userId, string workingDir = "/tmp/workspace")
    {
        UserId = userId;
        WorkingDirectory = workingDir;
        AgentRoot = Path.Combine(workingDir, ".agents");
        UserRoot = workingDir;
    }
}
