using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManInBlack.AI.Services;

/// <summary>
/// 全局事件总线，按 key 分发事件给对应订阅者。
/// <para>hook 通道使用 <see cref="HookKey"/>（"{agentId}::hook"），观察者通道使用 AgentId 原值，两条 lane 互不相见。</para>
/// <para>单个 handler 抛错只记日志，不影响其他 handler、不向上抛。</para>
/// </summary>
[ServiceRegister.Singleton]
public class EventBus
{
    private readonly ILogger<EventBus> _logger;

    /// <summary>无参构造（NullLogger），便于测试直接 new。DI 解析时使用注入 ILogger 的构造。</summary>
    public EventBus() : this(NullLogger<EventBus>.Instance) { }

    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger;
    }

    /// <summary>hook 通道 key：AgentId 追加 "::hook"，与观察者通道（AgentId 原值）隔离。</summary>
    public static string HookKey(string agentId) => $"{agentId}::hook";

    /// <summary>
    /// 订阅指定类型的事件，按 key 过滤
    /// </summary>
    public IDisposable Subscribe<TEvent>(string key, EventHandlerDelegate<TEvent> handler)
    {
        return EventBus<TEvent>.Subscribe(key, handler);
    }

    /// <summary>
    /// 按 key 广播事件给对应订阅者（单 handler 抛错被隔离，只记日志）
    /// </summary>
    public Task PublishAsync<TEvent>(string key, TEvent evt, CancellationToken cancellationToken = default)
    {
        return EventBus<TEvent>.PublishAsync(key, evt, _logger, cancellationToken);
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

    public static async Task PublishAsync(string key, TEvent evt, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (!HandlersByKey.TryGetValue(key, out var handlers))
            return;

        // 快照，避免持锁执行 handler
        List<EventHandlerDelegate<TEvent>> snapshot;
        lock (handlers)
        {
            snapshot = [.. handlers.Select(h => h.Handler)];
        }

        // 每个 handler 独立隔离：单个 handler 抛错只记日志，不影响其他 handler、不向上抛
        await Task.WhenAll(snapshot.Select(h => InvokeIsolated(key, h, evt, cancellationToken, logger)));
    }

    private static async Task InvokeIsolated(string key, EventHandlerDelegate<TEvent> handler, TEvent evt, CancellationToken cancellationToken, ILogger logger)
    {
        try
        {
            await handler(evt, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "EventBus handler 抛错（事件类型 {EventType}, key {Key}），已隔离，不影响其他订阅者",
                typeof(TEvent).Name, key);
        }
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
