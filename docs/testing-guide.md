# 中间件测试指北

本文档介绍如何为 ManInBlack 中间件编写单元测试，覆盖从简单到复杂的各种场景。

---

## 核心原理

中间件的 `HandleAsync` 签名是**纯函数式**的：

```
(context, next, ct) → IAsyncEnumerable<ChatResponseUpdate>
```

你只需要自己构造 `AgentContext`、自己实现 `next` 委托、自己收集输出流就能断言一切行为。

**不需要启动 HTTP 服务，不需要连接 AI API。**

---

## 基础套路

```csharp
// Arrange
var middleware = new MyMiddleware(/* 构造依赖 */);
var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
{
    AgentId   = "test-agent",
    UserInput = "hello",
    Messages  = [new(ChatRole.User, "hello")],
    Options   = new ChatOptions(),
};

ChatResponseUpdateHandler fakeNext = () => TestHelpers.AsyncSeq(
    new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("response")])
);

// Act
var results = await middleware.HandleAsync(ctx, fakeNext, ct).ToListAsync();

// Assert
Assert.Single(results);
Assert.Equal("response", results[0].Text);
```

---

## 辅助工具

项目在 `test/ManInBlack.AI.Tests/Helpers/` 下提供了三个辅助文件：

### TestHelpers.cs

| 成员                            | 说明                                                                 |
| ------------------------------- | -------------------------------------------------------------------- |
| `EmptyServiceProvider`          | 空 DI 容器，用于不需要依赖注入的中间件                               |
| `EmptyStream`                   | 空 IAsyncEnumerable（`AsyncEnumerable.Empty<ChatResponseUpdate>()`） |
| `AsyncSeq(params T[])`          | 将零个或多个元素包装为 IAsyncEnumerable                              |
| `ThrowOnMoveNext<T>(Exception)` | 第一次 MoveNextAsync 时抛出指定异常（用于测试重试逻辑）              |
| `ExtractTexts(this ...)`        | 从 ChatResponseUpdate 列表中提取所有 TextContent                     |

### FakeStorage.cs

提供内存版存储实现，无文件 I/O：

- **`FakeSessionStorage`** — 实现 `ISessionStorage`，内部用 `Dictionary<string, List<ChatMessage>>`
- **`FakeUserStorage`** — 实现 `IUserStorage`，可指定工作目录路径
- **`FakeUserWorkspace`** — 实现 `IUserWorkspace`，可定制

### FakeToolExecutor.cs

- **`FakeToolExecutor`** — 实现 `IToolExecutor`，记录调用次数和上下文，返回预设 `Result` 或 `Error`

---

## 五种测试模式

以下模式按复杂度递增排列，与 `docs/middleware-guide.md` 中的中间件模式一一对应。

---

### 模式一：纯 Context 转换型

这类中间件**只修改 context**，不查外部服务、不修改响应流。

**被测：SystemPromptInjectionMiddleware**

```csharp
[Fact]
public async Task HandleAsync_ShouldInsertSystemMessageAtHead()
{
    var middleware = new SystemPromptInjectionMiddleware(
        NullLogger<SystemPromptInjectionMiddleware>.Instance);
    var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
    {
        SystemPrompt = "You are a helpful assistant.",
        Messages = [new(ChatRole.User, "hello")]
    };

    await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

    Assert.Equal(ChatRole.System, ctx.Messages[0].Role);
    Assert.Equal("You are a helpful assistant.", ctx.Messages[0].Text);
}
```

**适用中间件：** SystemPromptInjection, UserInput, ContextCompress, Skill

**技巧：**
- 不需要 `ServiceProvider`，用 `EmptyServiceProvider` 即可
- `next` 用 `EmptyStream`
- 断言重点是 `context.Messages`、`context.SystemPrompt`、`context.Options` 的状态变化

---

### 模式二：包装集合型

这类中间件替换 `context.Messages` 为自定义集合，拦截 `Add` 操作。

**被测：MessageEnrichMiddleware**

```csharp
[Fact]
public async Task HandleAsync_ShouldSetCreatedAtOnNewMessage()
{
    var middleware = new MessageEnrichMiddleware();
    var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
    {
        Messages = [new(ChatRole.User, "hello")]
    };

    // next 内部添加消息，触发包装集合的 InsertItem
    ChatResponseUpdateHandler next = () =>
    {
        ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, "world"));
        return TestHelpers.EmptyStream;
    };

    await middleware.HandleAsync(ctx, next).ToListAsync();

    Assert.NotNull(ctx.Messages.Last().CreatedAt);
}
```

**关键点：**
- 在 `next` 内部执行 `ctx.Messages.Add()` 触发包装行为
- 验证副作用（如 `CreatedAt` 被补全）
- 验证 `context.Messages` 是否恢复为原始引用

**适用中间件：** MessageEnrich, SavePersistence

---

### 模式三：流拦截型

这类中间件不修改 context，只关注**流经的 ChatResponseUpdate**。

**被测：RetryMiddleware**

```csharp
[Fact]
public async Task HandleAsync_IOExceptionBeforeYield_ShouldRetry()
{
    var middleware = new RetryMiddleware(NullLogger<RetryMiddleware>.Instance);
    var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "test" };

    var callCount = 0;
    ChatResponseUpdateHandler next = () =>
    {
        callCount++;
        if (callCount == 1)
            return TestHelpers.ThrowOnMoveNext<ChatResponseUpdate>(
                new IOException("network error"));
        return TestHelpers.AsyncSeq(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("recovered")]));
    };

    var results = await middleware.HandleAsync(ctx, next,
        CancellationToken.None).ToListAsync();

    Assert.Equal(2, callCount);
    Assert.Contains(results.ExtractTexts(), t => t.Contains("retry"));
    Assert.Contains(results.ExtractTexts(), t => t.Contains("recovered"));
}
```

**ThrowOnMoveNext 的原理：**

```csharp
public static async IAsyncEnumerable<T> ThrowOnMoveNext<T>(Exception ex)
{
    throw ex;  // 在第一次 MoveNextAsync 时抛出
#pragma warning disable CS0162
    yield break; // 编译器要求的占位符
#pragma warning restore CS0162
}
```

**适用中间件：** Retry, Logging, ContextCompress

---

### 模式四：外部依赖型

这类中间件通过 `context.ServiceProvider.GetRequiredService<T>()` 获取依赖。测试时用 `ServiceCollection` 注入 Fake。

**被测：ReadPersistenceMiddleware**

```csharp
[Fact]
public async Task HandleAsync_ShouldRestoreSavedMessages()
{
    var storage = new FakeSessionStorage();
    await storage.SaveMessage("session_1",
        new(ChatRole.Assistant, "saved response"));

    var services = new ServiceCollection()
        .AddSingleton<ISessionStorage>(storage)
        .AddSingleton<IUserStorage>(new FakeUserStorage())
        .BuildServiceProvider();

    var middleware = new ReadPersistenceMiddleware();
    var ctx = new AgentContext(services)
    {
        SessionId = "session_1",
        ParentId  = "user_1",
        UserInput = "hello",
        Messages  = [new(ChatRole.User, "hello")]
    };

    await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream)
        .ToListAsync();

    Assert.Contains(ctx.Messages,
        m => m.Role == ChatRole.Assistant && m.Text == "saved response");
}
```

**关键点：**
- 用 `ServiceCollection` 构建真正的 ServiceProvider，注入 Fake
- Fake 保持简单 —— 纯内存，无文件 I/O
- 测试场景：
  - 无保存消息 → 正常继续
  - 有保存消息 → 恢复进 Messages
  - `/clear` / `/reset` / `/new` → 清空并 yield 确认
  - 消息含 reasoning → 被过滤

**适用中间件：** ReadPersistence, SavePersistence, AgentProfile

---

### 模式五：循环代理型

最复杂的场景 —— 模拟模型多次发起工具调用。

**被测：AgentLoopMiddleware**

```csharp
[Fact]
public async Task HandleAsync_WithToolCall_ShouldExecuteAndLoop()
{
    var executor = new FakeToolExecutor { Result = "file contents" };
    var logger = NullLogger<AgentContext>.Instance;
    var middleware = new AgentLoopMiddleware(executor, logger);
    var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
    {
        Messages = [new(ChatRole.User, "read file test.txt")]
    };

    var callCount = 0;
    ChatResponseUpdateHandler next = () =>
    {
        callCount++;
        if (callCount == 1)
        {
            // 第一轮：返回 tool call
            return TestHelpers.AsyncSeq(new ChatResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent("call_1", "ReadFile",
                    new Dictionary<string, object?> { ["path"] = "test.txt" })]
            ));
        }
        // 第二轮：最终文本
        return TestHelpers.AsyncSeq(new ChatResponseUpdate(
            ChatRole.Assistant, [new TextContent("done reading")]));
    };

    var results = await middleware.HandleAsync(ctx, next).ToListAsync();

    Assert.Equal(2, callCount);
    Assert.Equal(1, executor.ExecuteCount);
    Assert.Equal("ReadFile", executor.ExecutedContexts[0].ToolName);
    // 消息历史: user → assistant+tools → tool+results → assistant+text
    Assert.Equal(4, ctx.Messages.Count);
}
```

**关键点：**
- `next` 委托**每次调用返回不同的流**，模拟多轮对话
- 用 `callCount` 控制行为：
  - 第 1 次：返回 `FunctionCallContent`（工具调用）
  - 第 2 次：返回 `TextContent`（最终答案）
- `FakeToolExecutor` 返回预设 Result，验证 Execute 被调用
- 验证最终 `context.Messages` 包含完整的 user→assistant→tool→assistant 链
- 额外覆盖：Usage 累积、Reasoning 保存、Tool 报错处理

**适用中间件：** AgentLoopMiddleware

---

## 常见误区

1. **不要 mock `AgentContext`** — 它是普通类，直接 `new` 更直观
2. **不要 mock `ChatResponseUpdateHandler`** — 用 lambda 提供 IAsyncEnumerable
3. **`Assert.Contains` 的参数顺序是 `(expectedSubstring, actualString)`** — 写反了就是反模式
4. **测试 `next` 内部的操作** — 如验证持久化、消息包装，要在 `next` 内部执行 `ctx.Messages.Add()`
5. **不要用真实的文件系统** — 存储测试用 Fake 实现（`FakeSessionStorage` 等）代替

---

## 运行测试

```bash
# 全部测试
dotnet test test/ManInBlack.AI.Tests

# 只跑中间件测试
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~Middlewares"

# 只跑特定中间件
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AgentLoop"

# 只跑某个用例
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ShouldRetry"
```
