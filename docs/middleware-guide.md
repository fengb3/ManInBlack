# Middleware 开发指北

本文档介绍 ManInBlack 的中间件管道架构，以及如何编写自定义中间件。

---

## 核心概念

### 管道模型

中间件管道采用 **洋葱模型**：请求从外层向内传递，响应从内层向外返回。每个中间件持有 `next` 委托，决定何时（以及是否）将控制权交给下一个中间件。

```
Logging → Enrich → Skill → Profile → SystemPrompt → ReadPersistence
    ↓                                                              ↑
SavePersistence ← UserInput ← ContextCompress ← Tools ← AgentLoop ← IChatClient
```

`AgentPipelineBuilder.Build()` 以**逆序**包裹中间件，最终抵达 `IChatClient.GetStreamingResponseAsync()` 终点。

### 三个核心类型

| 类型                        | 文件                               | 职责                                                          |
| --------------------------- | ---------------------------------- | ------------------------------------------------------------- |
| `AgentMiddleware`           | Core/Middleware/AgentMiddleware.cs | 抽象基类，定义 `HandleAsync`                                  |
| `AgentContext`              | Core/Middleware/AgentContext.cs    | 请求上下文，在管道中传递                                      |
| `ChatResponseUpdateHandler` | Core/Middleware/AgentMiddleware.cs | `delegate IAsyncEnumerable<ChatResponseUpdate>`，代表管道下游 |

---

## AgentContext 属性速查

| 属性                      | 类型                          | 用途                                                             |
| ------------------------- | ----------------------------- | ---------------------------------------------------------------- |
| `Messages`                | `IList<ChatMessage>`          | 聊天消息列表，中间件可读写                                       |
| `Options`                 | `ChatOptions?`                | 模型调用选项（Tools、Temperature 等）                            |
| `SystemPrompt`            | `string`                      | 系统提示词，在 `SystemPromptInjectionMiddleware` 中拼入 Messages |
| `UserInput`               | `string`                      | 本轮用户输入原文                                                 |
| `Items`                   | `IDictionary<string, object>` | 中间件间共享状态字典                                             |
| `ServiceProvider`         | `IServiceProvider`            | DI 容器，用于解析服务                                            |
| `AgentId`                 | `string`                      | Agent 实例标识                                                   |
| `ParentId` / `ParentType` | `string`                      | 父级标识和类型（用户或另一个 Agent）                             |
| `AccumulatedUsage`        | `UsageDetails`                | 累积的 Token 用量                                                |
| `CancellationToken`       | `CancellationToken`           | 取消令牌                                                         |

---

## 编写中间件

### 最小模板

```csharp
using System.Runtime.CompilerServices;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

[ServiceRegister.Scoped]  // 必须标记，用于 DI 注册
public class MyMiddleware : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1️⃣ 前置处理：修改 context

        // 2️⃣ 调用下游并透传流式响应
        await foreach (var update in next().WithCancellation(ct))
            yield return update;

        // 3️⃣ 后置处理
    }
}
```

**要点：**

- 继承 `AgentMiddleware`，重写 `HandleAsync`
- 标记 `[ServiceRegister.Scoped]` 以注册到 DI 容器
- 类**不需要**标记 `partial`（除非使用了源生成器的其他功能）
- `HandleAsync` 返回 `IAsyncEnumerable<ChatResponseUpdate>`，使用 `yield return` 逐条输出

### 带依赖注入

通过构造函数注入服务：

```csharp
[ServiceRegister.Scoped]
public class MyMiddleware(ILogger<MyMiddleware> logger, IMyService myService) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        logger.LogInformation("Processing for agent {AgentId}", context.AgentId);
        // ...
        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }
}
```

---

## 常见模式

### 模式一：请求前修改 Context

在调用 `next()` 之前修改 `Messages`、`Options`、`SystemPrompt` 等。

**示例 — 注入工具声明**（参考 `CommandToolMiddleware`）：

```csharp
public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
    AgentContext context, ChatResponseUpdateHandler next,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    context.Options ??= new ChatOptions();
    context.Options.Tools ??= [];
    foreach (var tool in MyTools.Declarations)
        context.Options.Tools.Add(tool);

    await foreach (var update in next().WithCancellation(ct))
        yield return update;
}
```

**示例 — 注入系统提示词**（参考 `AgentProfileMiddleware`）：

```csharp
public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
    AgentContext context, ChatResponseUpdateHandler next,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    context.SystemPrompt += "\n\n## Additional Instructions\n...";
    await foreach (var update in next().WithCancellation(ct))
        yield return update;
}
```

> **注意：** 修改 `SystemPrompt` 的中间件必须在 `SystemPromptInjectionMiddleware` **之前**执行（即在管道中位于更外层），否则修改不会被注入到消息列表中。

### 模式二：拦截并修改响应流

在 `await foreach` 循环中对流式响应做变换。

**示例 — 追踪 Token 用量：**

```csharp
public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
    AgentContext context, ChatResponseUpdateHandler next,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var update in next().WithCancellation(ct))
    {
        // 过滤、转换或记录 update
        if (update.Contents.OfType<UsageContent>().FirstOrDefault() is { } usage)
        {
            // 处理用量信息
        }
        yield return update;
    }
}
```

### 模式三：包装 Messages 集合

替换 `context.Messages` 为自定义集合，拦截所有添加操作。此模式用于在消息生命周期中自动执行逻辑。

**示例 — 自动补充元数据**（参考 `MessageEnrichMiddleware`）：

```csharp
public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
    AgentContext context, ChatResponseUpdateHandler next,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var original = context.Messages;
    context.Messages = new EnrichingCollection(original);

    await foreach (var update in next().WithCancellation(ct))
        yield return update;

    context.Messages = original; // 恢复原始引用（已包含所有消息）
}

private class EnrichingCollection : Collection<ChatMessage>
{
    public EnrichingCollection(IList<ChatMessage> list) : base(list) { }

    protected override void InsertItem(int index, ChatMessage item)
    {
        item.CreatedAt ??= DateTimeOffset.UtcNow; // 补全元数据
        base.InsertItem(index, item);
    }
}
```

> **注意：** 在方法结束时恢复 `context.Messages = original`，确保后续中间件看到的是统一的消息列表。

### 模式四：条件中断管道

不调用 `next()`，直接 `yield return` 后 `yield break`。

**示例 — 命令拦截**（参考 `ReadPersistenceMiddleware`）：

```csharp
public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
    AgentContext context, ChatResponseUpdateHandler next,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    if (IsResetCommand(context.UserInput))
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("已重置")],
        };
        yield break; // 不调用 next()，管道终止
    }

    await foreach (var update in next().WithCancellation(ct))
        yield return update;
}
```

### 模式五：循环代理（Agent Loop）

模型返回 `FunctionCallContent` 时，执行工具并将结果追加回消息列表，再次调用 `next()` 循环处理，直到模型不再发起工具调用。

**核心结构**（参考 `AgentLoopMiddleware`）：

```csharp
public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
    AgentContext context, ChatResponseUpdateHandler next,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    while (true)
    {
        var functionCalls = new List<FunctionCallContent>();
        var textBuilder = new StringBuilder();

        // 收集本轮响应
        await foreach (var update in next().WithCancellation(ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent fcc: functionCalls.Add(fcc); break;
                    case TextContent text: textBuilder.Append(text.Text); break;
                    case UsageContent usage: context.AccumulatedUsage.Add(usage.Details); break;
                }
            }
            yield return update;
        }

        // 追加 assistant 消息
        var assistantContents = new List<AIContent>();
        if (textBuilder.Length > 0) assistantContents.Add(new TextContent(textBuilder.ToString()));
        assistantContents.AddRange(functionCalls);
        if (assistantContents.Count > 0)
            context.Messages.Add(new ChatMessage(ChatRole.Assistant, assistantContents));

        // 没有工具调用则结束
        if (functionCalls.Count == 0) yield break;

        // 执行工具调用并追加结果
        var toolResults = new List<AIContent>();
        foreach (var fc in functionCalls)
        {
            var result = await ExecuteTool(fc, ct);
            toolResults.Add(new FunctionResultContent(fc.CallId, result));
            yield return new ChatResponseUpdate(ChatRole.Tool, [toolResults[^1]]);
        }
        context.Messages.Add(new ChatMessage(ChatRole.Tool, toolResults));

        // 继续循环，让模型处理工具结果
    }
}
```

---

## 注册与管道顺序

### 注册方式

在 `AgentPipelineBuilderExtensions.UseDefault()` 中通过 `Use<TMiddleware>()` 添加：

```csharp
builder
    .Use<LoggingMiddleware>()
    .Use<MyNewMiddleware>()        // 添加到合适的位置
    .Use<AgentLoopMiddleware>();   // AgentLoop 始终在最后
```

也可通过 `Use(instance)` 注册实例：

```csharp
builder.Use(new MyStatelessMiddleware());
```

### 管道顺序与执行方向

`Use()` 注册的顺序 = 洋葱模型从外到内的顺序。注册在前的中间件：

- 先执行前置逻辑
- 后收到响应

```
.Use<A>()  →  .Use<B>()  →  .Use<C>()  →  IChatClient

执行顺序：A.前置 → B.前置 → C.前置 → ChatClient → C.后置 → B.后置 → A.后置
```

### 默认管道顺序

| #   | 中间件                            | 职责                               |
| --- | --------------------------------- | ---------------------------------- |
| 1   | `LoggingMiddleware`               | 记录输入/输出日志                  |
| 2   | `MessageEnrichMiddleware`         | 为消息补全 `CreatedAt` 元数据      |
| 3   | `SkillMiddleware`                 | 注入技能描述和工具声明             |
| 4   | `AgentProfileMiddleware`          | 读取 Markdown 配置注入系统提示词   |
| 5   | `SystemPromptInjectionMiddleware` | 将 `SystemPrompt` 插入消息列表开头 |
| 6   | `ReadPersistenceMiddleware`       | 加载历史消息，处理 reset 命令      |
| 7   | `SavePersistenceMiddleware`       | 自动持久化新增消息                 |
| 8   | `UserInputMiddleware`             | 将 `UserInput` 追加为用户消息      |
| 9   | `ContextCompressMiddleware`       | 压缩旧的工具结果                   |
| 10  | `CommandToolMiddleware`           | 注入命令行工具声明                 |
| 11  | `FileToolMiddleware`              | 注入文件操作工具声明               |
| 12  | `AgentLoopMiddleware`             | 工具调用循环（必须在最后）         |

### 顺序规则

- **修改 `SystemPrompt` 的中间件** 必须在 `SystemPromptInjectionMiddleware` 之前
- **修改 `UserInput` 的中间件** 必须在 `UserInputMiddleware` 之前
- **添加 Tool 声明的中间件** 必须在 `AgentLoopMiddleware` 之前
- **`AgentLoopMiddleware`** 必须是最后一个中间件
- **持久化相关中间件** 包裹管道中部，确保所有消息变更都被捕获

---

## Items 字典：中间件间通信

使用 `context.Items` 在中间件间传递数据：

```csharp
// 中间件 A：写入
context.Items["my_key"] = someValue;

// 中间件 B：读取
if (context.Items.TryGetValue("my_key", out var value))
{
    var typed = (MyType)value;
    // ...
}
```

---

## 注意事项

1. **不要阻塞流**：`HandleAsync` 是 `IAsyncEnumerable`，必须用 `yield return` 逐条输出，不要先收集再一次性返回。
2. **使用 `WithCancellation(ct)`**：`next().WithCancellation(ct)` 确保取消信号正确传播。
3. **`[EnumeratorCancellation]`**：参数上的此特性确保迭代器被 `WithCancellation` 调用时能收到取消令牌。
4. **DI 生命周期**：所有中间件注册为 `Scoped`，每次请求创建新实例。
5. **线程安全**：`AgentContext` 不是线程安全的，不要在中间件内启动并发操作访问它。
6. **`yield break` 终止管道**：不调用 `next()` 直接 `yield break` 可以短路管道，但要确保消费者能正确处理提前结束的情况。
