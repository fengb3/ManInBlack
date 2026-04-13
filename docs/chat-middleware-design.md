# 对话中间件系统设计方案

## Context

ManInBlack 目前只提供了底层的 `IChatClient` 适配器（发送请求、接收响应），缺少上层对话管理能力：对话历史维护、上下文窗口控制、多轮工具调用编排等。本方案设计一套自建的中间件管道系统，**不依赖 M.E.AI 的 DelegatingChatClient/ChatClientBuilder**，采用与现有 `ToolCallFilter` 一致的 `ExecuteAsync(context, next)` 模式，并结合源生成器简化开发。

**设计原则：流式优先**。`ChatSession` 主入口返回 `IAsyncEnumerable<ChatResponseUpdate>`，非流式 `SendMessageAsync` 在流式之上通过收集实现。

---

## 核心架构

### 管道流程

```
ChatSession.StreamMessageAsync(userMessage)
  │
  ├─ 构建管道 (reverse-wrap，与 ToolCaller 模式一致)
  │  pipeline = CoreInvokeAsync                    // 最内层：调用 IChatClient
  │  pipeline = mw[i].ExecuteAsync(ctx, pipeline)  // 反向包裹中间件
  │
  ├─ await pipeline(context)                       // 执行管道，设置 context.ResponseStream
  │
  └─ return context.ResponseStream                 // 返回（可能被中间件包装过的）流

中间件执行顺序（按 Order 升序）:
  [Order -1000] LoggingMiddleware       → 记录请求，包装流以记录完成
  [Order  100] HistoryWindowMiddleware  → 裁剪历史后调用 next
  [Order  500] ToolCallOrchestrator     → 包装流，自动处理工具调用循环
  [Order 1000+] 自定义中间件...
  [Core]       CoreInvokeAsync          → 调用 IChatClient.GetStreamingResponseAsync
```

### 关键设计：流式中间件

中间件调用 `next(context)` 后，`context.ResponseStream` 被设置为底层返回的流。中间件可以**用自己的 async iterator 包装这个流**，实现：

- **LoggingMiddleware**：转发所有 update，流结束后记录日志
- **ToolCallOrchestrator**：转发文本 update，累积工具调用片段，流结束后执行工具，再发起下一轮流

```csharp
// 中间件包装流的模式
public override async Task ExecuteAsync(ChatContext context, Func<ChatContext, Task> next)
{
    // 前置处理（修改请求参数）
    await next(context);
    
    // 后置处理（包装响应流）
    var innerStream = context.ResponseStream;
    context.ResponseStream = MyStreamWrapper(innerStream, context);
}

private async IAsyncEnumerable<ChatResponseUpdate> MyStreamWrapper(
    IAsyncEnumerable<ChatResponseUpdate> inner, ChatContext ctx,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var update in inner.WithCancellation(ct))
        yield return update;
    // 流结束后的逻辑
}
```

---

## 新增类型

### 1. `ChatContext` — 管道执行上下文

**文件**: `ManInBlack.AI/Chat/ChatContext.cs`

```csharp
public class ChatContext
{
    public IServiceProvider ServiceProvider { get; }
    public string SessionId { get; }
    public List<ChatMessage> History { get; set; }       // 当前对话历史（中间件可修改）
    public ChatMessage UserMessage { get; set; }          // 本轮用户消息
    public ChatOptions Options { get; set; }              // 聊天选项
    public IAsyncEnumerable<ChatResponseUpdate> ResponseStream { get; set; }  // 响应流
    public CancellationToken CancellationToken { get; set; }
    public Dictionary<string, object?> Items { get; }     // 中间件间传递数据
    public int ToolCallRound { get; set; }
    public int MaxToolCallRounds { get; set; } = 10;
}
```

### 2. `ChatMiddleware` — 抽象中间件基类

**文件**: `ManInBlack.AI/Chat/ChatMiddleware.cs`

```csharp
public abstract class ChatMiddleware
{
    /// <summary>
    /// 执行顺序，数值越小越先执行
    /// </summary>
    public abstract int Order { get; }

    /// <summary>
    /// 处理聊天请求。
    /// 调用 next(context) 后，context.ResponseStream 被设置为响应流。
    /// 中间件可以修改请求参数或包装响应流。
    /// </summary>
    public abstract Task ExecuteAsync(ChatContext context, Func<ChatContext, Task> next);
}
```

### 3. `ChatSession` — 会话管理器

**文件**: `ManInBlack.AI/Chat/ChatSession.cs`

```csharp
public class ChatSession
{
    // 主入口：流式
    public IAsyncEnumerable<ChatResponseUpdate> StreamMessageAsync(
        ChatMessage userMessage, ChatOptions? options, CancellationToken ct);

    // 便捷方法：非流式（在 StreamMessageAsync 之上收集）
    public async Task<ChatResponse> SendMessageAsync(
        ChatMessage userMessage, ChatOptions? options, CancellationToken ct);

    // 核心：调用 IChatClient.GetStreamingResponseAsync
    private Task CoreInvokeAsync(ChatContext context)
    {
        context.ResponseStream = _chatClient.GetStreamingResponseAsync(
            context.History, context.Options, context.CancellationToken);
        return Task.CompletedTask;
    }
}
```

### 4. 内置中间件

| 中间件 | Order | 文件 | 职责 |
|--------|-------|------|------|
| `LoggingMiddleware` | -1000 | `Chat/Middleware/LoggingMiddleware.cs` | 记录请求/响应日志 |
| `HistoryWindowMiddleware` | 100 | `Chat/Middleware/HistoryWindowMiddleware.cs` | 按 `IHistoryTrimStrategy` 裁剪历史 |
| `ToolCallOrchestratorMiddleware` | 500 | `Chat/Middleware/ToolCallOrchestratorMiddleware.cs` | 多轮工具调用编排 |

### 5. 历史裁剪策略

| 策略 | 文件 | 方式 |
|------|------|------|
| `IHistoryTrimStrategy` | `Chat/History/IHistoryTrimStrategy.cs` | 策略接口 |
| `CountBasedTrimStrategy` | `Chat/History/CountBasedTrimStrategy.cs` | 按消息数量，保留系统提示 + 最近 N 条 |
| `TokenEstimateTrimStrategy` | `Chat/History/TokenEstimateTrimStrategy.cs` | 按字符数估算 token，不依赖 provider tokenizer |
| `SummarizingTrimStrategy` | `Chat/History/SummarizingTrimStrategy.cs` | 用轻量模型对旧消息生成摘要 |

### 6. 工厂 & DI

**文件**: `ManInBlack.AI/Chat/ChatSessionFactory.cs`

```csharp
public class ChatSessionFactory
{
    public ChatSession Create(IChatClient chatClient);
    // 从 IServiceProvider 解析所有 ChatMiddleware，按 Order 排序注入
}
```

---

## ToolCallOrchestrator 流式编排

这是最复杂的中间件，核心逻辑：

```csharp
public override Task ExecuteAsync(ChatContext context, Func<ChatContext, Task> next)
{
    // 用 async iterator 替换流，透明处理工具调用循环
    context.ResponseStream = OrchestrateAsync(context, next);
    return Task.CompletedTask;
}

private async IAsyncEnumerable<ChatResponseUpdate> OrchestrateAsync(
    ChatContext context, Func<ChatContext, Task> next,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    for (var round = 0; round < context.MaxToolCallRounds; round++)
    {
        await next(context);  // 调用 CoreInvokeAsync，设置新的 ResponseStream

        var hasToolCalls = false;
        var toolCallAccumulator = /* 累积器 */;

        await foreach (var update in context.ResponseStream.WithCancellation(ct))
        {
            if (update.Contents 有 FunctionCallContent)
            {
                hasToolCalls = true;
                累积工具调用片段;
            }
            else
            {
                yield return update;  // 文本 update 立即转发
            }
        }

        if (!hasToolCalls) yield break;  // 无工具调用，结束

        // 执行工具调用（通过现有 ToolCaller）
        var results = await ExecuteToolsAsync(accumulated, context);

        // 追加到历史
        context.History.Add(assistantMessage);
        context.History.Add(toolResultMessage);

        // 循环：下一轮 next(context) 会发起新请求
    }
}
```

**关键特性**：
- 文本内容立即转发，用户无需等待工具调用完成
- 工具调用片段被累积，不转发给调用者（调用者只看到最终文本）
- 与现有 `ToolCaller`（源生成）集成，复用 `ToolCallFilter` 管道
- 多轮工具调用对调用者完全透明

---

## 源生成器

### 新增属性

| 属性 | 文件 | 目标 |
|------|------|------|
| `[ChatMiddleware(Order = N)]` | `Attributes/ChatMiddlewareAttribute.cs` | 标记中间件类，自动注册 DI |
| `[ChatSessionConfig]` | `Attributes/ChatSessionConfigAttribute.cs` | 标记 partial 类，生成会话工厂方法 |

### ChatMiddlewareGenerator

扫描 `[ChatMiddleware]` 类，生成 `ChatMiddlewareRegistrationExtensions.g.cs`：

```csharp
// 生成的代码
public static class ChatMiddlewareRegistrationExtensions
{
    public static IServiceCollection AddChatMiddleware(this IServiceCollection services)
    {
        services.AddSingleton<MyApp.LoggingMiddleware>();         // Order -1000
        services.AddSingleton<MyApp.HistoryWindowMiddleware>();    // Order 100
        services.AddSingleton<MyApp.ToolCallOrchestratorMiddleware>(); // Order 500
        return services;
    }

    public static IEnumerable<ChatMiddleware> GetOrderedChatMiddleware(
        this IServiceProvider sp)
    {
        // 按 Order 排序返回所有注册的中间件
    }
}
```

### ChatSessionConfigGenerator

扫描 `[ChatSessionConfig]` partial 类，生成 `CreateSession` 工厂方法：

```csharp
// 用户代码
[ChatSessionConfig(Name = "MathAgent", MaxToolCallRounds = 5)]
public partial class MathAgent { }

// 生成的代码
partial class MathAgent
{
    public static ChatSession CreateSession(IServiceProvider sp, IChatClient chatClient)
    {
        var middleware = sp.GetOrderedChatMiddleware();
        var session = new ChatSession(chatClient, sp, middleware);
        session.MaxToolCallRounds = 5;
        return session;
    }
}
```

### 诊断规则

| ID | 级别 | 触发条件 |
|----|------|---------|
| MIB020 | Error | `[ChatMiddleware]` 类未继承 `ChatMiddleware` |
| MIB021 | Error | `[ChatMiddleware]` 类是 abstract 或 static |
| MIB022 | Error | `[ChatSessionConfig]` 类不是 partial |

---

## 文件结构

```
ManInBlack.AI/
  Attributes/
    AiToolAttribute.cs                  (已有)
    ChatMiddlewareAttribute.cs           (新增)
    ChatSessionConfigAttribute.cs        (新增)
    ServiceRegister.cs                  (已有)
  Chat/
    ChatContext.cs                       (新增)
    ChatMiddleware.cs                    (新增)
    ChatSession.cs                       (新增)
    ChatSessionFactory.cs                (新增)
    History/
      IHistoryTrimStrategy.cs            (新增)
      CountBasedTrimStrategy.cs          (新增)
      TokenEstimateTrimStrategy.cs       (新增)
      SummarizingTrimStrategy.cs         (新增)
    Middleware/
      LoggingMiddleware.cs               (新增)
      HistoryWindowMiddleware.cs         (新增)
      ToolCallOrchestratorMiddleware.cs  (新增)

ManInBlack.AI.SourceGenerator/
  ChatMiddlewareGenerator.cs             (新增)
  ChatMiddlewareEmitter.cs               (新增)
  ChatMiddlewareModel.cs                 (新增)
  ChatSessionConfigGenerator.cs          (新增)
  ChatSessionConfigEmitter.cs            (新增)
  ChatSessionConfigModel.cs              (新增)
```

---

## 实现阶段

### Phase 1: 核心抽象 + 内置中间件
- `ChatContext`, `ChatMiddleware`, `ChatSession`, `ChatSessionFactory`
- `LoggingMiddleware`
- `IHistoryTrimStrategy` + `CountBasedTrimStrategy`
- `HistoryWindowMiddleware`
- `ToolCallOrchestratorMiddleware`
- 在 Playground 中手动测试

### Phase 2: 高级历史策略
- `TokenEstimateTrimStrategy`
- `SummarizingTrimStrategy`

### Phase 3: 源生成器
- `ChatMiddlewareAttribute` + `ChatMiddlewareGenerator/Emitter`
- `ChatSessionConfigAttribute` + `ChatSessionConfigGenerator/Emitter`
- 诊断规则 MIB020-MIB022

### Phase 4: 集成验证
- 在 Playground 中完整示例
- `dotnet build` 验证

---

## 开发者使用示例

```csharp
// 1. 定义中间件
[ChatMiddleware(Order = 200)]
public class RateLimitMiddleware : ChatMiddleware
{
    public override int Order => 200;
    public override async Task ExecuteAsync(ChatContext context, Func<ChatContext, Task> next)
    {
        await Task.Delay(100, context.CancellationToken);
        await next(context);
    }
}

// 2. 定义会话
[ChatSessionConfig(Name = "MathAgent", MaxToolCallRounds = 5)]
public partial class MathAgent { }

// 3. DI 注册
services.AddChatMiddleware();  // 源生成
services.AddAutoRegisteredServices();  // 现有
services.AddSingleton<IHistoryTrimStrategy>(new TokenEstimateTrimStrategy(8000));

// 4. 使用
var session = MathAgent.CreateSession(sp, chatClient);

// 流式
await foreach (var update in session.StreamMessageAsync(msg, options, ct))
    Console.Write(update.Text);

// 或非流式
var response = await session.SendMessageAsync(msg, options, ct);
```

---

## 与现有代码的集成点

- **`ToolCaller`**（源生成）→ `ToolCallOrchestratorMiddleware` 通过 DI 获取并调用 `CallTool()`
- **`ToolCallFilter`**（现有管道）→ 在 `ToolCaller.CallTool()` 内部执行，无需修改
- **`AllToolDeclarations`**（源生成）→ 用户在 `ChatOptions.Tools` 中使用
- **`IChatClient` 适配器** → `ChatSession` 通过构造函数接收，不修改
- **`IModelProvider` / `ModelChoice`** → 用户自行创建 `IChatClient` 传给 `ChatSession`
- **`ServiceRegistrationGenerator`** → 独立存在，互不影响
