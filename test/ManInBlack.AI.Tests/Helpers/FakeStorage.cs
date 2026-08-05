using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Storage;
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
    private readonly Dictionary<string, List<(string? SessionId, SessionSource Source, DateTime LastAt)>> _sessions = new();

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

    public Task<string> CreateNewSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        var user = GetOrCreateUser(userId).GetAwaiter().GetResult();
        var sid = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        if (!_sessions.ContainsKey(userId)) _sessions[userId] = new();
        _sessions[userId].Add((sid, source, DateTime.UtcNow));
        return Task.FromResult(sid);
    }

    public Task<string?> GetLatestSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        if (!_sessions.TryGetValue(userId, out var list))
            return Task.FromResult<string?>(null);
        var latest = list.Where(x => x.Source == source).OrderByDescending(x => x.LastAt).FirstOrDefault();
        return Task.FromResult(latest.SessionId);
    }
}

/// <summary>
/// 内存版 IUserWorkspace
/// </summary>
public class FakeUserWorkspace : IUserWorkspace
{
    public string AgentRoot { get; }
    public string WorkingDirectory { get; set; }

    public FakeUserWorkspace(string userId, string workingDir = "/tmp/workspace")
    {
        WorkingDirectory = workingDir;
        AgentRoot = Path.Combine(workingDir, ".agents");
    }
}

/// <summary>
/// 内存版 IAgentStateStorage，用 Dictionary 代替文件 I/O
/// </summary>
public class FakeAgentStateStorage : IAgentStateStorage
{
    private readonly Dictionary<string, List<ChatMessage>> _messages = new();
    private readonly Dictionary<string, AgentStateSnapshot> _snapshots = new();

    public Task SaveMessage(string sessionId, ChatMessage message)
    {
        if (!_messages.TryGetValue(sessionId, out var list))
        {
            list = [];
            _messages[sessionId] = list;
        }
        list.Add(message);
        return Task.CompletedTask;
    }

    public Task<IList<ChatMessage>> LoadMessages(string sessionId)
    {
        if (_messages.TryGetValue(sessionId, out var list))
            return Task.FromResult<IList<ChatMessage>>([.. list]);
        return Task.FromResult<IList<ChatMessage>>([]);
    }

    public Task<AgentStateSnapshot?> LoadSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        _snapshots.TryGetValue(sessionId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot, CancellationToken ct = default)
    {
        _snapshots[sessionId] = snapshot;
        return Task.CompletedTask;
    }

    public Task DeleteSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        _snapshots.Remove(sessionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取所有快照，用于断言
    /// </summary>
    public IReadOnlyDictionary<string, AgentStateSnapshot> AllSnapshots => _snapshots;
}
