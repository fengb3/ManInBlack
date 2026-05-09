using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Attributes;

namespace ManInBlack.AI.Services;

/// <summary>
/// 全局事件总线，按 key 分发事件给对应订阅者
/// </summary>
[ServiceRegister.Singleton]
public class EventBus
{
    /// <summary>
    /// 订阅指定类型的事件，按 key 过滤
    /// </summary>
    public IDisposable Subscribe<TEvent>(string key, EventHandlerDelegate<TEvent> handler)
    {
        return EventBus<TEvent>.Subscribe(key, handler);
    }

    /// <summary>
    /// 按 key 广播事件给对应订阅者
    /// </summary>
    public Task PublishAsync<TEvent>(string key, TEvent evt, CancellationToken cancellationToken = default)
    {
        return EventBus<TEvent>.PublishAsync(key, evt, cancellationToken);
    }
}

public delegate Task EventHandlerDelegate<in TEvent>(TEvent evt, CancellationToken cancellationToken = default);

/// <summary>
/// 按事件类型分组的静态广播器，以 key 隔离不同订阅者
/// </summary>
public static class EventBus<TEvent>
{
    private static readonly ConcurrentDictionary<string, List<HandlerEntry>> HandlersByKey = new();

    public static IDisposable Subscribe(string key, EventHandlerDelegate<TEvent> handler)
    {
        var entry = new HandlerEntry(handler);
        var handlers = HandlersByKey.GetOrAdd(key, _ => []);
        lock (handlers)
        {
            handlers.Add(entry);
        }
        return new Subscription(entry, key);
    }

    public static async Task PublishAsync(string key, TEvent evt, CancellationToken cancellationToken = default)
    {
        if (!HandlersByKey.TryGetValue(key, out var handlers))
            return;

        // 快照，避免持锁执行 handler
        List<EventHandlerDelegate<TEvent>> snapshot;
        lock (handlers)
        {
            snapshot = [.. handlers.Select(h => h.Handler)];
        }

        await Task.WhenAll(snapshot.Select(h => h(evt, cancellationToken)));
    }

    private static void Remove(string key, HandlerEntry entry)
    {
        if (!HandlersByKey.TryGetValue(key, out var handlers))
            return;

        lock (handlers)
        {
            handlers.Remove(entry);
            if (handlers.Count == 0)
                HandlersByKey.TryRemove(key, out _);
        }
    }

    private sealed class HandlerEntry(EventHandlerDelegate<TEvent> handler)
    {
        public EventHandlerDelegate<TEvent> Handler { get; } = handler;
    }

    private sealed class Subscription(HandlerEntry entry, string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Remove(key, entry);
        }
    }
}
