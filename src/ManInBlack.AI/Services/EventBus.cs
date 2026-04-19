using System.Collections.Concurrent;
using ManInBlack.AI.Core.Attributes;

namespace ManInBlack.AI.Services;

/// <summary>
/// 作用域标识，每个 DI Scope 内唯一
/// </summary>
[ServiceRegister.Scoped]
public class ScopeId
{
    public string Id { get; } = Guid.NewGuid().ToString();

    public override string ToString() => Id;

    public override bool Equals(object? obj) => obj is ScopeId other && Id == other.Id;

    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// 作用域级别的事件总线，将事件广播给同一 Scope 内的所有订阅者
/// </summary>
[ServiceRegister.Scoped]
public class EventBus(ScopeId scopeId) : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    /// <summary>
    /// 订阅指定类型的事件
    /// </summary>
    public IDisposable Subscribe<TEvent>(EventHandlerDelegate<TEvent> handler)
    {
        var subscription = EventBus<TEvent>.Subscribe(handler, scopeId);
        _subscriptions.Add(subscription);
        return subscription;
    }

    /// <summary>
    /// 广播事件给同一 Scope 内的所有订阅者
    /// </summary>
    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default)
    {
        return EventBus<TEvent>.PublishAsync(evt, scopeId, cancellationToken);
    }

    public void Dispose()
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
    }
}

public delegate Task EventHandlerDelegate<in TEvent>(TEvent evt, CancellationToken cancellationToken = default);

/// <summary>
/// 按事件类型分组的静态广播器，以 scopeId 隔离不同作用域的订阅者
/// </summary>
public static class EventBus<TEvent>
{
    private static readonly ConcurrentDictionary<string, List<HandlerEntry>> HandlersByScope = new();

    public static IDisposable Subscribe(EventHandlerDelegate<TEvent> handler, ScopeId scope)
    {
        var entry = new HandlerEntry(handler);
        var handlers = HandlersByScope.GetOrAdd(scope.Id, _ => []);
        lock (handlers)
        {
            handlers.Add(entry);
        }
        return new Subscription(entry, scope.Id);
    }

    public static async Task PublishAsync(TEvent evt, ScopeId scope, CancellationToken cancellationToken = default)
    {
        if (!HandlersByScope.TryGetValue(scope.Id, out var handlers))
            return;

        // 快照，避免持锁执行 handler
        List<EventHandlerDelegate<TEvent>> snapshot;
        lock (handlers)
        {
            snapshot = [.. handlers.Select(h => h.Handler)];
        }

        await Task.WhenAll(snapshot.Select(h => h(evt, cancellationToken)));
    }

    private static void Remove(string scopeId, HandlerEntry entry)
    {
        if (!HandlersByScope.TryGetValue(scopeId, out var handlers))
            return;

        lock (handlers)
        {
            handlers.Remove(entry);
            if (handlers.Count == 0)
                HandlersByScope.TryRemove(scopeId, out _);
        }
    }

    private sealed class HandlerEntry(EventHandlerDelegate<TEvent> handler)
    {
        public EventHandlerDelegate<TEvent> Handler { get; } = handler;
    }

    private sealed class Subscription(HandlerEntry entry, string scopeId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Remove(scopeId, entry);
        }
    }
}
