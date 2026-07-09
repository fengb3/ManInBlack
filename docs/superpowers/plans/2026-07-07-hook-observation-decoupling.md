# Hook / 观察者 解耦 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 hook 订阅/发布挪到独立 key（`{agentId}::hook`），与观察者通道（`agentId`）隔离；并给 `EventBus` 加 per-handler 错误隔离，让观察者抛错不再炸链。

**Architecture:** `EventBus` 新增静态 `HookKey(agentId)` helper；所有 hook 订阅与 hook-point 发布改走 `HookKey`，观察者（飞书卡片 / Console / SubAgent / ModelContent）保持 `agentId` 不变。`PublishAsync` 对每个 handler 包 try/catch + log，单个 handler 抛错不影响其他 handler、不向上抛。事件类型与 demo 全部不动。

**Tech Stack:** C# / .NET, xUnit, Microsoft.Extensions.AI, Microsoft.Extensions.Logging

**Spec:** `docs/superpowers/specs/2026-07-07-hook-observation-decoupling-design.md`

---

## File Structure

| 文件 | 责任 | 改动 |
|---|---|---|
| `src/ManInBlack.AI/Services/EventBus.cs` | 事件总线 | 新增 `HookKey` helper + per-handler 错误隔离 + 注入 `ILogger` |
| `src/ManInBlack.AI/Middlewares/HookMiddleware.cs` | hook 订阅 + BeforeLlmCall/AgentCompleted 发布 | 6 个订阅 + 2 个发布改 `HookKey` |
| `src/ManInBlack.AI/ToolCallFilters/AgentLifecycleFilter.cs` | Before/AfterToolExecute 发布 | 改双 lane 发布（hook lane 先 await 读 `IsBlocked`，再 observer lane） |
| `src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs` | AfterLlmCall/AllToolsCompleted 发布 | 2 个发布改 `HookKey` |
| `test/ManInBlack.AI.Tests/EventBusTests.cs` | EventBus 测试 | 加 `HookKey` + 错误隔离用例 |
| `test/ManInBlack.AI.Tests/Middlewares/HookMiddlewareTests.cs` | HookMiddleware 测试 | 加「BeforeLlmCall 不触达观察者 lane」用例 |
| `test/ManInBlack.AI.Tests/Middlewares/AgentLifecycleFilterTests.cs` | AgentLifecycleFilter 测试 | 模拟 hook 的订阅改 `HookKey`；加观察者 lane 用例 |
| `test/ManInBlack.AI.Tests/Middlewares/AgentLoopMiddlewareTests.cs` | AgentLoopMiddleware 测试 | AfterLlmCall/AllToolsCompleted 订阅改 `HookKey` |

不动：`Events/AgentLifecycleEvent.cs`（事件类型）、`EventPublishingMiddleware.cs`、`DelegationTools.cs`、所有 demo。

---

## Task 1: EventBus — HookKey helper + per-handler 错误隔离 + ILogger

**Files:**
- Modify: `src/ManInBlack.AI/Services/EventBus.cs`
- Test: `test/ManInBlack.AI.Tests/EventBusTests.cs`

- [ ] **Step 1: 写失败的测试（追加到 `EventBusTests` 类末尾，即最后一个 `}` 之前）**

在 `EventBusTests.cs` 的 `private record TestEvent(string Message);` 之后、类结束前，追加：

```csharp
[Fact]
public void HookKey_Appends_Suffix()
{
    Assert.Equal("agent-1::hook", EventBus.HookKey("agent-1"));
}

[Fact]
public async Task HookKey_Isolates_Hook_Lane_From_Observer_Lane()
{
    var hookReceived = new List<TestEvent>();
    var observerReceived = new List<TestEvent>();
    var agentId = "agent-x";

    using var h = _bus.Subscribe<TestEvent>(EventBus.HookKey(agentId),
        (e, _) => { hookReceived.Add(e); return Task.CompletedTask; });
    using var o = _bus.Subscribe<TestEvent>(agentId,
        (e, _) => { observerReceived.Add(e); return Task.CompletedTask; });

    await _bus.PublishAsync(EventBus.HookKey(agentId), new TestEvent("to-hook"));
    await _bus.PublishAsync(agentId, new TestEvent("to-observer"));

    Assert.Single(hookReceived);
    Assert.Equal("to-hook", hookReceived[0].Message);
    Assert.Single(observerReceived);
    Assert.Equal("to-observer", observerReceived[0].Message);
}

[Fact]
public async Task PublishAsync_HandlerThrows_DoesNotThrow_And_OthersStillRun()
{
    var received = new List<TestEvent>();
    using var sub1 = _bus.Subscribe<TestEvent>("key1",
        (e, _) => { received.Add(e); return Task.CompletedTask; });
    using var sub2 = _bus.Subscribe<TestEvent>("key1",
        (_, _) => throw new InvalidOperationException("boom"));

    // 不应抛异常
    await _bus.PublishAsync("key1", new TestEvent("hello"));

    // 抛错的 handler 不影响其他 handler
    Assert.Single(received);
    Assert.Equal("hello", received[0].Message);
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~EventBusTests"`
Expected: FAIL —— `HookKey_Appends_Suffix` / `HookKey_Isolates_Hook_Lane_From_Observer_Lane` 编译错误（`EventBus.HookKey` 不存在）；`PublishAsync_HandlerThrows_DoesNotThrow_And_OthersStillRun` 抛 `AggregateException`（handler 抛错未被隔离）。

- [ ] **Step 3: 实现 EventBus（整文件替换为下面内容）**

替换 `src/ManInBlack.AI/Services/EventBus.cs` 全文：

```csharp
using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Attributes;
using Microsoft.Extensions.Logging;

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
```

- [ ] **Step 4: 确认没有直接调用静态 `EventBus<TEvent>.PublishAsync` 的地方（签名加了 `ILogger` 参数）**

Run: `grep -rn "EventBus<.*>\.PublishAsync" src test` （或用 ripgrep：`rg "EventBus<.+>\.PublishAsync" src test`）
Expected: 无输出（只有实例 `bus.PublishAsync` / `eventBus.PublishAsync` 被使用，无直接静态调用）。若有命中，需同步补上 `ILogger` 参数。

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~EventBusTests"`
Expected: PASS（全部 EventBusTests 用例，含新增 3 个）。

- [ ] **Step 6: Commit**

```bash
git add src/ManInBlack.AI/Services/EventBus.cs test/ManInBlack.AI.Tests/EventBusTests.cs
git commit -m "♻️ EventBus: HookKey 双通道 + per-handler 错误隔离"
```

---

## Task 2: HookMiddleware — 订阅 + BeforeLlmCall/AgentCompleted 发布改 HookKey

**Files:**
- Modify: `src/ManInBlack.AI/Middlewares/HookMiddleware.cs`
- Test: `test/ManInBlack.AI.Tests/Middlewares/HookMiddlewareTests.cs`

- [ ] **Step 1: 写失败的测试（追加到 `HookMiddlewareTests` 类末尾）**

先在 `HookMiddlewareTests.cs` 顶部 using 区加（若尚无）：

```csharp
using ManInBlack.AI.Events;
```

再在类末尾追加：

```csharp
[Fact]
public async Task HandleAsync_BeforeLlmCall_DoesNotReachObserverLane()
{
    var observerReceived = new List<BeforeLlmCallEvent>();
    var bus = new EventBus();
    bus.Subscribe<BeforeLlmCallEvent>("agent-obs",
        (evt, _) => { observerReceived.Add(evt); return Task.CompletedTask; });

    var fakeExecutor = new FakeHookExecutor();
    var middleware = new HookMiddleware(fakeExecutor, NullLogger<HookMiddleware>.Instance);
    var ctx = new AgentContext(BuildSp(bus))
    {
        AgentId = "agent-obs",
        SystemPrompt = "s",
        UserInput = "u",
    };

    _ = await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream, CancellationToken.None)
        .ToListAsync();

    // hook 走 ::hook lane，观察者 lane（agentId）不应收到 BeforeLlmCall
    Assert.Empty(observerReceived);
    // 但 BeforeLlmCall hook 仍触发
    Assert.Contains(fakeExecutor.ExecutedHooks, h => h.Point == HookPoint.BeforeLlmCall);
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~HookMiddlewareTests.HandleAsync_BeforeLlmCall_DoesNotReachObserverLane"`
Expected: FAIL —— 当前 `HookMiddleware` 把 `BeforeLlmCall` 发到 `agentId`，观察者 lane 会收到，`Assert.Empty(observerReceived)` 失败。

- [ ] **Step 3: 改 HookMiddleware —— 6 个订阅改 HookKey**

在 `src/ManInBlack.AI/Middlewares/HookMiddleware.cs` 中，把 6 处 `bus.Subscribe<XxxEvent>(key, ...)` 的第一个参数 `key` 全部改为 `EventBus.HookKey(key)`。涉及：

- `bus.Subscribe<BeforeLlmCallEvent>(key, ...)`  → `bus.Subscribe<BeforeLlmCallEvent>(EventBus.HookKey(key), ...)`
- `bus.Subscribe<AfterLlmCallEvent>(key, ...)`   → `EventBus.HookKey(key)`
- `bus.Subscribe<BeforeToolExecuteEvent>(key, ...)` → `EventBus.HookKey(key)`
- `bus.Subscribe<AfterToolExecuteEvent>(key, ...)`  → `EventBus.HookKey(key)`
- `bus.Subscribe<AllToolsCompletedEvent>(key, ...)` → `EventBus.HookKey(key)`
- `bus.Subscribe<AgentCompletedEvent>(key, ...)`    → `EventBus.HookKey(key)`

（`using ManInBlack.AI.Services;` 已存在，`EventBus.HookKey` 可直接调用。）

- [ ] **Step 4: 改 HookMiddleware —— 2 个发布改 HookKey**

把 `BeforeLlmCall` 发布（`await bus.PublishAsync(key, beforeEvt, ct);`）改为：

```csharp
await bus.PublishAsync(EventBus.HookKey(key), beforeEvt, ct);
```

把 `AgentCompleted` 发布（`await bus.PublishAsync(key, new AgentCompletedEvent { ... }, ct);`）改为：

```csharp
await bus.PublishAsync(EventBus.HookKey(key), new AgentCompletedEvent
{
    AgentId = key,
    SystemPrompt = context.SystemPrompt,
    UserInput = context.UserInput,
}, ct);
```

- [ ] **Step 5: 核对 6 个订阅都用了 HookKey（防止漏改无单测覆盖的 4 个 tool/loop 订阅）**

Run: `rg -n "bus\.Subscribe<" src/ManInBlack.AI/Middlewares/HookMiddleware.cs`
Expected: 6 行命中，且每行的第一个参数都是 `EventBus.HookKey(key)`，不应有裸 `key`。

- [ ] **Step 6: 跑 HookMiddleware 测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~HookMiddlewareTests"`
Expected: PASS（含新增用例 + 既有 6 个；BeforeLlmCall 注入 / AgentCompleted 触发等行为保持不变）。

- [ ] **Step 7: Commit**

```bash
git add src/ManInBlack.AI/Middlewares/HookMiddleware.cs test/ManInBlack.AI.Tests/Middlewares/HookMiddlewareTests.cs
git commit -m "♻️ HookMiddleware: 订阅与发布走 ::hook lane"
```

---

## Task 3: AgentLifecycleFilter — Before/AfterToolExecute 双 lane 发布

**Files:**
- Modify: `src/ManInBlack.AI/ToolCallFilters/AgentLifecycleFilter.cs`
- Test: `test/ManInBlack.AI.Tests/Middlewares/AgentLifecycleFilterTests.cs`

- [ ] **Step 1: 更新测试里「模拟 hook」的订阅 key（改 HookKey），并加观察者 lane 用例**

在 `AgentLifecycleFilterTests.cs` 中，把所有 `bus.Subscribe<BeforeToolExecuteEvent>("test-agent", ...)` 与 `bus.Subscribe<AfterToolExecuteEvent>("test-agent", ...)` 的第一个参数 `"test-agent"` 改为 `EventBus.HookKey("test-agent")`。这些是模拟 hook 的订阅，必须与 hook lane 对齐。（`using ManInBlack.AI.Services;` 已存在。）

涉及位置（按出现顺序）：
- `Setup` helper 内的 `BeforeToolExecuteEvent` 与 `AfterToolExecuteEvent` 两处订阅
- `ExecuteAsync_BeforeHook_Blocked_ShouldNotCallNext` 内 `BeforeToolExecuteEvent` 订阅
- `ExecuteAsync_BeforeHook_Blocked_ShouldSetErrorMessage` 内 `BeforeToolExecuteEvent` 订阅
- `ExecuteAsync_BeforeHook_NotBlocked_ShouldCallNext` 内 `BeforeToolExecuteEvent` 与 `AfterToolExecuteEvent` 订阅
- `ExecuteAsync_AfterHook_ShouldFireWithResult` 内两处订阅
- `ExecuteAsync_AfterHook_ShouldFireEvenOnError` 内两处订阅

替换模式（每处都一样）：
```csharp
// before
bus.Subscribe<BeforeToolExecuteEvent>("test-agent", async (evt, ct) => ...
// after
bus.Subscribe<BeforeToolExecuteEvent>(EventBus.HookKey("test-agent"), async (evt, ct) => ...
```
（`AfterToolExecuteEvent` 同理。）

再在类末尾追加观察者 lane 回归用例：

```csharp
[Fact]
public async Task ExecuteAsync_BeforeTool_ObserverLane_ReceivesEvent()
{
    var bus = new EventBus();
    var agentCtx = new AgentContext(
        new ServiceCollection().AddSingleton<EventBus>(bus).BuildServiceProvider())
    { AgentId = "test-agent" };
    var serviceProvider = new ServiceCollection()
        .AddSingleton<EventBus>(bus)
        .AddSingleton(agentCtx)
        .BuildServiceProvider();

    var observerReceived = new List<BeforeToolExecuteEvent>();
    // 观察者订阅在 agentId（观察者 lane）
    using var obs = bus.Subscribe<BeforeToolExecuteEvent>("test-agent",
        (evt, _) => { observerReceived.Add(evt); return Task.CompletedTask; });

    var filter = new AgentLifecycleFilter(bus, NullLogger<AgentLifecycleFilter>.Instance);
    var ctx = new ToolExecuteContext(serviceProvider)
    {
        ToolName = "ObserverTool",
        CallId = "call-obs",
    };
    Task Next(ToolExecuteContext c) => Task.CompletedTask;

    await filter.ExecuteAsync(ctx, Next);

    Assert.Single(observerReceived);
    Assert.Equal("ObserverTool", observerReceived[0].ToolName);
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AgentLifecycleFilterTests"`
Expected: FAIL —— 订阅已挪到 `HookKey`，但 `AgentLifecycleFilter` 仍只发 `agentId`，模拟 hook 收不到 → 阻断/触发断言失败。

- [ ] **Step 3: 改 AgentLifecycleFilter 为双 lane 发布**

替换 `src/ManInBlack.AI/ToolCallFilters/AgentLifecycleFilter.cs` 中 `ExecuteAsync` 的发布段。在构造 `beforeEvt` 之后、原本 `await eventBus.PublishAsync(key, beforeEvt, default);` 的位置，改为：

```csharp
var hookKey = EventBus.HookKey(key);

// ── BeforeToolExecute 事件 ──
// hook lane 先跑（await），读 IsBlocked；再 observer lane，让观察者看到带 IsBlocked 的事件
await eventBus.PublishAsync(hookKey, beforeEvt, default);
await eventBus.PublishAsync(key, beforeEvt, default);

if (beforeEvt.IsBlocked)
{
    logger.LogWarning("[AgentLifecycleFilter] 工具 {ToolName} 被阻断：{Reason}", context.ToolName,
        beforeEvt.BlockReason);
    context.Error = new InvalidOperationException(
        beforeEvt.BlockReason ?? "Blocked by AgentLifecycleFilter"
    );
    return;
}
```

把结尾的 `AfterToolExecute` 发布（原本单条 `await eventBus.PublishAsync(key, new AfterToolExecuteEvent { ... }, default);`）改为双 lane：

```csharp
// ── AfterToolExecute 事件（hook lane + observer lane）──
var afterEvt = new AfterToolExecuteEvent
{
    AgentId = key,
    ToolName = context.ToolName,
    CallId = context.CallId,
    ArgumentsJson = argsJson,
    ResultJson = context.Result?.ToString(),
    Error = context.Error?.Message,
};
await eventBus.PublishAsync(hookKey, afterEvt, default);
await eventBus.PublishAsync(key, afterEvt, default);
```

（`using ManInBlack.AI.Services;` 已存在，`EventBus.HookKey` 可直接调用。）

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AgentLifecycleFilterTests"`
Expected: PASS（含新增观察者 lane 用例 + 既有阻断/触发用例）。

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/ToolCallFilters/AgentLifecycleFilter.cs test/ManInBlack.AI.Tests/Middlewares/AgentLifecycleFilterTests.cs
git commit -m "♻️ AgentLifecycleFilter: Before/AfterToolExecute 走双 lane(hook + observer)"
```

---

## Task 4: AgentLoopMiddleware — AfterLlmCall/AllToolsCompleted 发布改 HookKey

**Files:**
- Modify: `src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs`
- Test: `test/ManInBlack.AI.Tests/Middlewares/AgentLoopMiddlewareTests.cs`

- [ ] **Step 1: 更新测试订阅 key**

在 `AgentLoopMiddlewareTests.cs` 的 `HandleAsync_ShouldPublishAfterLlmCallAndAllToolsCompletedEvents` 中，把两处订阅的第一个参数 `"test-agent"` 改为 `EventBus.HookKey("test-agent")`：

```csharp
bus.Subscribe<AfterLlmCallEvent>(EventBus.HookKey("test-agent"), (evt, ct) => ...
bus.Subscribe<AllToolsCompletedEvent>(EventBus.HookKey("test-agent"), (evt, ct) => ...
```

确认文件顶部有 `using ManInBlack.AI.Services;`（`AgentLoopMiddlewareTests` 若无则补上）。

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AgentLoopMiddlewareTests.HandleAsync_ShouldPublishAfterLlmCallAndAllToolsCompletedEvents"`
Expected: FAIL —— 订阅挪到 `HookKey`，但 `AgentLoopMiddleware` 仍发 `agentId`，断言计数为 0。

- [ ] **Step 3: 改 AgentLoopMiddleware 的 2 个发布**

在 `src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs` 中：

`AfterLlmCall` 发布（`await bus.PublishAsync(key, new AfterLlmCallEvent { ... }, ct);`）改为：

```csharp
await bus.PublishAsync(EventBus.HookKey(key), new AfterLlmCallEvent
{
    AgentId = key,
    SystemPrompt = context.SystemPrompt,
    UserInput = context.UserInput,
}, ct);
```

`AllToolsCompleted` 发布（`await bus.PublishAsync(key, new AllToolsCompletedEvent { ... }, ct);`）改为：

```csharp
await bus.PublishAsync(EventBus.HookKey(key), new AllToolsCompletedEvent
{
    AgentId = key,
}, ct);
```

（`using ManInBlack.AI.Services;` 已存在。）

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AgentLoopMiddlewareTests"`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs test/ManInBlack.AI.Tests/Middlewares/AgentLoopMiddlewareTests.cs
git commit -m "♻️ AgentLoopMiddleware: AfterLlmCall/AllToolsCompleted 走 ::hook lane"
```

---

## Task 5: 全量构建 + 全量测试 + 收尾核对

**Files:** 无（验证为主）

- [ ] **Step 1: 全量构建**

Run: `dotnet build`
Expected: 成功，0 error。

- [ ] **Step 2: 全量测试**

Run: `dotnet test test/ManInBlack.AI.Tests`
Expected: 全部 PASS。

- [ ] **Step 3: 核对未遗漏的生命周期发布点**

Run: `rg -n "PublishAsync\(key," src/ManInBlack.AI` 与 `rg -n "PublishAsync\(\s*key\b" src/ManInBlack.AI`
Expected: `src/ManInBlack.AI` 下不应再有把 hook 事件发到裸 `key`（agentId）的发布点。
- `EventPublishingMiddleware` 发 `ModelContentEvent` 到 `key` —— **正确**（无 hook，观察者专用）。
- `DelegationTools` 发 `SubAgent*Event` 到 `key` —— **正确**（无 hook）。
- `AgentLifecycleFilter` 发 `BeforeToolExecute/AfterToolExecute` 到 `key` —— **正确**（这是 observer lane 的那次，hook lane 已发 `HookKey`）。
- `HookMiddleware` / `AgentLoopMiddleware` 的 hook 事件发布应全是 `EventBus.HookKey(key)`，不应有裸 `key`。

- [ ] **Step 4: 核对 demo 未被改动**

Run: `git diff --stat demo/`
Expected: 空输出（demo 目录零改动）。若有改动，回退。

- [ ] **Step 5: 若 Step 1-4 有任何修复，提交**

```bash
git add -A
git commit -m "✅ hook/观察者解耦: 全量测试通过"
```

（若无改动则跳过本步。）
