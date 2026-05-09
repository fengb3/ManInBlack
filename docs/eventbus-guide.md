# 事件总线指南

> 本文档是 AGENTS.md 的子文档，修改事件系统相关代码前应先阅读此文档。

## 概述

ManInBlack 通过 `EventBus` 实现组件间的事件通信。EventBus 是一个**全局单例**，按 key（通常是 `AgentId`）隔离不同 Agent 的事件，支持类型安全的发布/订阅。

核心特性：

- **按 key 隔离**：只有订阅了相同 key 的处理器才能收到事件
- **按类型隔离**：不同事件类型互不干扰，同一 key 可以同时订阅多种事件
- **异步处理**：所有 handler 都是 `async`，支持 `CancellationToken`
- **安全清理**：订阅返回 `IDisposable`，Dispose 后不再收到事件

---

## 事件类型一览

### 模型输出事件

| 事件 | 时机 | 发布者 |
|---|---|---|
| `ModelContentEvent` (Text) | 模型输出文本片段 | `EventPublishingMiddleware` |
| `ModelContentEvent` (Reasoning) | 模型输出推理内容 | `EventPublishingMiddleware` |
| `ModelContentEvent` (Usage) | 模型返回 Token 用量 | `EventPublishingMiddleware` |
| `ModelContentEvent` (Completed) | 模型流式输出结束 | `EventPublishingMiddleware` |

### 工具执行事件

| 事件 | 时机 | 发布者 |
|---|---|---|
| `BeforeToolExecuteEvent` | 工具执行前，支持阻断 | `AgentLifecycleFilter` |
| `AfterToolExecuteEvent` | 工具执行后 | `AgentLifecycleFilter` |

### Agent 生命周期事件

| 事件 | 时机 | 发布者 |
|---|---|---|
| `BeforeLlmCallEvent` | LLM 调用前，支持注入文本 | `HookMiddleware` |
| `AfterLlmCallEvent` | LLM 响应后 | `HookMiddleware` |
| `BeforeToolExecuteEvent` | 工具执行前，支持阻断 | `AgentLifecycleFilter` |
| `AfterToolExecuteEvent` | 工具执行后 | `AgentLifecycleFilter` |
| `AllToolsCompletedEvent` | 本批次所有工具执行完毕 | `HookMiddleware` |
| `AgentCompletedEvent` | Agent 循环结束 | `HookMiddleware` |

---

## 事件详情

### ModelContentEvent

模型流式输出事件，由 `EventPublishingMiddleware` 发布。

```csharp
public record ModelContentEvent
{
    public string AgentId { get; init; }
    public ModelContentKind Kind { get; init; }   // Text / Reasoning / Usage / Completed
    public string? Text { get; init; }             // Text、Reasoning 时有值
    public UsageDetails? Usage { get; init; }      // Usage 时有值
}
```

### BeforeToolExecuteEvent / AfterToolExecuteEvent

工具执行生命周期事件，由 `AgentLifecycleFilter` 发布。

`BeforeToolExecuteEvent` 支持阻断，订阅者设置 `IsBlocked = true` 可阻止工具执行。

```csharp
public record BeforeToolExecuteEvent
{
    public string AgentId { get; init; }
    public string ToolName { get; init; }
    public string CallId { get; init; }
    public string? ArgumentsJson { get; init; }
    public bool IsBlocked { get; set; }        // 由订阅者设置
    public string? BlockReason { get; set; }   // 阻断原因
}
```

### BeforeLlmCallEvent

LLM 调用前事件，**支持注入文本**。订阅者向 `InjectedTexts` 追加内容。

```csharp
public record BeforeLlmCallEvent
{
    public string AgentId { get; init; }
    public string? SystemPrompt { get; init; }
    public string? UserInput { get; init; }
    public List<string> InjectedTexts { get; }  // 由订阅者追加
    public string? InjectTarget { get; set; }   // "SystemPrompt" | "UserMessage"
}
```

---

## 使用方式

### 订阅事件

在 `AgentFactory.RunAsync` 的 `configure` 回调中订阅（必须在 Scope 内）：

```csharp
var updates = factory.RunAsync("agent", input, userId, "console", ctx =>
{
    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
    var key = ctx.AgentId;

    // 订阅工具执行事件
    bus.Subscribe<BeforeToolExecuteEvent>(key, async (evt, ct) =>
    {
        Console.WriteLine($"[工具调用] {evt.ToolName}");
    });

    // 订阅模型输出事件
    bus.Subscribe<ModelContentEvent>(key, async (evt, ct) =>
    {
        if (evt.Kind == ModelContentKind.Text)
            Console.Write(evt.Text);
    });
});
```

### 发布自定义事件

```csharp
// 定义事件类型
public record MyCustomEvent(string Message);

// 发布
await eventBus.PublishAsync(agentId, new MyCustomEvent("hello"));

// 订阅
bus.Subscribe<MyCustomEvent>(agentId, async (evt, ct) =>
{
    Console.WriteLine(evt.Message);
});
```

### 清理订阅

`Subscribe` 返回 `IDisposable`，用完后 Dispose：

```csharp
IDisposable? sub = null;

var updates = factory.RunAsync("agent", input, userId, "console", ctx =>
{
    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
    sub = bus.Subscribe<BeforeToolExecuteEvent>(ctx.AgentId, (evt, ct) =>
    {
        Console.WriteLine(evt.ToolName);
        return Task.CompletedTask;
    });
});

await foreach (var update in updates) { /* ... */ }

sub?.Dispose();
```

---

## 作用域隔离

EventBus 本身是全局单例，但通过 key（`AgentId`）隔离事件。只有**相同 key** 的订阅者才能收到事件：

```csharp
// ✅ 正确：在 configure 回调中订阅，使用 ctx.AgentId 作为 key
var updates = factory.RunAsync("agent", input, userId, "console", ctx =>
{
    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
    bus.Subscribe<AfterToolExecuteEvent>(ctx.AgentId, handler);
});

// ❌ 错误：在 Scope 外订阅，key 不匹配，收不到事件
var bus = rootSp.GetRequiredService<EventBus>();
bus.Subscribe<AfterToolExecuteEvent>("wrong-key", handler);
```

不同事件类型之间也是隔离的：同一 key 可以同时订阅 `BeforeToolExecuteEvent` 和 `ModelContentEvent`，互不干扰。Dispose 其中一个不影响另一个。

---

## 发布者一览

| 组件 | 注册方式 | 发布的事件 | 管道位置 |
|---|---|---|---|
| `EventPublishingMiddleware` | Scoped | `ModelContentEvent` | 中间件管道内，包裹 LLM 调用 |
| `AgentLifecycleFilter` | Scoped | `BeforeToolExecuteEvent`、`AfterToolExecuteEvent` | 工具过滤器管道内 |
| `HookMiddleware` | Scoped | `BeforeLlmCallEvent`、`AfterLlmCallEvent`、`AllToolsCompletedEvent`、`AgentCompletedEvent` | 中间件管道内 |

---

## 文件清单

| 文件 | 说明 |
|---|---|
| `src/ManInBlack.AI/Services/EventBus.cs` | EventBus 核心：Subscribe/Publish，按 key + 类型双重隔离 |
| `src/ManInBlack.AI/Events/ModelContentEvent.cs` | 模型输出事件和 `ModelContentKind` 枚举 |
| `src/ManInBlack.AI/Events/AgentLifecycleEvent.cs` | Agent 生命周期事件（6 种） |
| `src/ManInBlack.AI/ToolCallFilters/AgentLifecycleFilter.cs` | 工具执行生命周期过滤器，发布 BeforeToolExecute / AfterToolExecute 事件 |
| `src/ManInBlack.AI/Middlewares/EventPublishingMiddleware.cs` | 模型流式输出事件发布者 |
| `test/ManInBlack.AI.Tests/EventBusTests.cs` | EventBus 单元测试 |

---

## 注意事项

- **必须在 Scope 内订阅**：EventBus 是单例，但事件按 `AgentId` 隔离。在 `configure` 回调中通过 `ctx.ServiceProvider` 获取 EventBus 并订阅
- **Dispose 防止泄漏**：订阅返回 `IDisposable`，用完必须 Dispose。支持多次 Dispose（幂等）
- **发布不阻塞流**：`EventPublishingMiddleware` 通过 `yield return` 流式转发 LLM 响应，事件发布不会阻塞流式输出
- **快照执行**：`PublishAsync` 对 handler 列表做快照后执行，避免持锁调用 handler
- **发布到空 key 安全**：如果某个 key 没有任何订阅者，`PublishAsync` 直接返回，不抛异常
