using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Storage;

/// <summary>
/// 内存版 <see cref="IAgentStateStorage"/>，用于不安装 SQLite 持久化包时的默认行为。
/// </summary>
public sealed class InMemoryAgentStateStorage : IAgentStateStorage
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _messages = new();
    private readonly ConcurrentDictionary<string, AgentStateSnapshot> _snapshots = new();

    public Task SaveMessage(string sessionId, ChatMessage message)
    {
        var list = _messages.GetOrAdd(sessionId, static _ => new List<ChatMessage>());
        lock (list)
        {
            list.Add(message);
        }
        return Task.CompletedTask;
    }

    public Task<IList<ChatMessage>> LoadMessages(string sessionId)
    {
        if (!_messages.TryGetValue(sessionId, out var list))
            return Task.FromResult<IList<ChatMessage>>(Array.Empty<ChatMessage>());
        lock (list)
        {
            return Task.FromResult<IList<ChatMessage>>(list.ToList());
        }
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
        _snapshots.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
