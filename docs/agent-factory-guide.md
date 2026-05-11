# Agent 工厂指南

本文档介绍 `AgentFactory` 的设计、注册方式和运行机制。`AgentFactory` 是 Agent 的运行时入口，负责管理定义、构建管道、追踪执行、管理作用域。

---

## 一句话总结

`AgentFactory` 是一个 **Singleton** 服务，封装了 Agent 的完整生命周期：从 DI 中收集定义、按名称构建管道、创建 Scope 隔离执行、追踪同用户并发。

---

## 核心概念

### AgentDefinition — Agent 预设

`AgentDefinition` 是一个 POCO，描述一个 Agent 的静态配置：

| 属性               | 类型       | 默认值     | 说明                                     |
| ------------------ | ---------- | ---------- | ---------------------------------------- |
| `Name`             | `string`   | 必填       | 唯一标识，`RunAsync` 按此查找            |
| `Description`      | `string`   | `""`       | Agent 描述                               |
| `Instruction`      | `string`   | `""`       | 系统提示词，赋值给 `AgentContext.SystemPrompt` |
| `PipelineName`     | `string`   | `"default"` | 管道名称，决定使用哪套中间件组合         |
| `ParentAgentName`  | `string?`  | `null`     | 父 Agent 名称（可选，用于多 Agent 编排） |
| `SubAgents`        | `List<string>` | `[]`   | 可委托的子 Agent 名称列表 |
| `ModelChoiceName`  | `string?`  | `null`     | 引用的 ModelChoice 名称。不填则使用全局默认 ModelChoice |

### 管道注册 — 命名管道配置委托

每个管道对应一个 `Func<AgentPipelineBuilder, AgentPipelineBuilder>` 委托。Factory 内置两个预设：

- `"default"` — `builder.UseDefault()`，完整管道（含工具、持久化、压缩等）
- `"simple"` — `builder.UseSimple()`，精简管道（不含工具和持久化）

自定义管道通过 `factory.RegisterPipeline()` 注册。

### 执行追踪 — 同用户并发管理

`AgentFactory` 内部维护一个 `ConcurrentDictionary<string, CancellationTokenSource>` 跟踪正在执行的 Agent。当同一用户的新消息到来时，自动取消该用户正在运行的旧 Agent。

### 作用域管理 — DI Scope 自动创建和释放

`RunAsync` 每次调用时自动创建 `IServiceScope`，在 `finally` 中释放。这意味着每次 Agent 运行都有独立的 Scoped 服务实例（`AgentContext`、`IChatClient` 等），不会互相干扰。`EventBus` 是 Singleton 服务，全局共享，通过 key（`AgentId`）隔离不同 Agent 的事件。

---

## 注册 Agent 定义

### 方式一：通过 settings.json 配置（推荐）

在 `~/.man-in-black/settings.json` 中添加 `Agents` 节：

```json
{
  "Agents": {
    "translator": {
      "Description": "翻译专家，擅长将文本翻译成各种语言",
      "Instruction": "你是一个翻译专家...",
      "PipelineName": "sub-agent",
      "ModelChoiceName": "deepseek-chat"
    },
    "console-agent": {
      "Instruction": "你是一个AI助手...",
      "PipelineName": "default",
      "SubAgents": ["translator"]
    }
  }
}
```

使用 `AddManInBlackFromSettings()` 或 `AddManInBlackFromConfiguration()` 时，Agents 节中的定义会自动注册到 DI，无需额外代码。

### 方式二：通过代码注册

通过 DI 扩展方法 `AddAgentDefinition()` 注册：

```csharp
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;

var services = new ServiceCollection();

// 注册核心服务
services.AddManInBlackFromSettings();

// 注册 Agent 定义
services.AddAgentDefinition(new AgentDefinition
{
    Name = "my-agent",
    Instruction = "你是一个AI助手，可以用工具帮助用户完成任务。请用中文回复。",
    PipelineName = "default"
});
```

`AddAgentDefinition()` 将 `AgentDefinition` 注册为 **Singleton**。`AgentFactory` 构造时自动从 DI 中收集所有 `IEnumerable<AgentDefinition>` 并注册到内部字典。

> **注意：** 如果 settings.json 和代码中注册了同名 Agent，`AgentFactory` 会抛出 `ArgumentException`。

> **注意：** 同名 Agent 会抛出 `ArgumentException`。确保每个定义的 `Name` 唯一。

---

## 注册自定义管道

### RegisterPipeline 方法

```csharp
/// <summary>
/// 注册管道配置委托
/// </summary>
/// <param name="name">管道名称</param>
/// <param name="configure">配置委托，接收 AgentPipelineBuilder 并返回配置后的 builder</param>
void RegisterPipeline(string name, Func<AgentPipelineBuilder, AgentPipelineBuilder> configure)
```

### 使用场景

当内置的 `"default"` 和 `"simple"` 不满足需求时，可以注册自定义管道。例如在飞书场景中注入一个自定义中间件：

```csharp
// 在 WebApplication.Build() 之后获取 Factory
var factory = app.Services.GetRequiredService<AgentFactory>();

// 注册飞书自定义管道
factory.RegisterPipeline("feishu", pipeline => pipeline
    .Use<MyCustomMiddleware>()    // 自定义中间件
    .UseDefault());               // 接上默认管道

// 对应的 Agent 定义需要指定 PipelineName = "feishu"
```

> **注意：** `RegisterPipeline` 是**覆盖式注册**。如果名称已存在，新委托会替换旧的。内置的 `"default"` 和 `"simple"` 也可以被覆盖，但通常不建议这样做。

---

## 运行 Agent

### RunAsync 方法签名

```csharp
/// <summary>
/// 运行指定 Agent，返回流式响应更新
/// </summary>
/// <param name="agentName">Agent 名称（对应 AgentDefinition.Name）</param>
/// <param name="userInput">用户输入文本</param>
/// <param name="parentId">父级标识（通常是用户 ID）</param>
/// <param name="parentType">父级类型（例如 "feishu_user"、"console"）</param>
/// <param name="configure">可选回调，让调用者微调 AgentContext</param>
/// <param name="ct">取消令牌</param>
/// <returns>流式响应更新</returns>
public async IAsyncEnumerable<ChatResponseUpdate> RunAsync(
    string agentName,
    string userInput,
    string parentId,
    string parentType,
    Action<AgentContext>? configure = null,
    CancellationToken ct = default)
```

### 参数说明

| 参数         | 类型                     | 说明                                                     |
| ------------ | ------------------------ | -------------------------------------------------------- |
| `agentName`  | `string`                 | Agent 名称，必须在 Factory 中已注册                      |
| `userInput`  | `string`                 | 用户输入的文本                                           |
| `parentId`   | `string`                 | 用户标识，用于持久化、会话管理和执行追踪                 |
| `parentType` | `string`                 | 用户来源类型，标识调用方类型                             |
| `configure`  | `Action<AgentContext>?`  | 可选回调，在管道构建前微调上下文                         |
| `ct`         | `CancellationToken`      | 取消令牌，传入后绑定到 `AgentContext.CancellationToken` |

### 基本用法

```csharp
var factory = serviceProvider.GetRequiredService<AgentFactory>();

var updates = factory.RunAsync(
    "my-agent",       // agent 名称
    "帮我查看文件列表", // 用户输入
    "user-123",       // 用户 ID
    "console"         // 来源类型
);

await foreach (var update in updates)
{
    foreach (var content in update.Contents)
    {
        if (content is TextContent text)
            Console.Write(text.Text);
    }
}
```

### configure 回调的用途

`configure` 回调在 `AgentContext` 初始化之后、管道构建之前执行。常见用途：

**1. 动态修改系统提示词**

```csharp
var updates = factory.RunAsync(
    "feishu-agent", userInput, userId, "feishu_user",
    ctx => ctx.SystemPrompt += $"""

        <system>
        你的面对的用户的飞书 open id 是: {openId}
        </system>
        """,
    cts.Token);
```

**2. 在 Factory Scope 内订阅 EventBus**

```csharp
EventBus? bus = null;
IDisposable? subscription = null;

var updates = factory.RunAsync(
    "console-agent", args[0], "console", "Default",
    ctx =>
    {
        // 在 Factory 的 scope 内订阅，使用 ctx.AgentId 作为 key
        bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
        var key = ctx.AgentId;
        subscription = bus.Subscribe<BeforeToolExecuteEvent>(key, async (@event, ct) =>
        {
            Console.WriteLine($"[Tool Call] {@event.ToolName}");
        });
    });

// 消费完成后清理
await foreach (var _ in updates) { }
subscription?.Dispose();
```

> **重要：** EventBus 的 `Subscribe` 必须在 `configure` 回调中执行（即 Factory 创建的 Scope 内），使用 `ctx.AgentId` 作为 key，才能收到当前 Agent 的事件。在 Scope 外订阅或使用错误的 key 会收不到任何事件。详见[作用域生命周期](#作用域生命周期)。

---

## 执行追踪

### 为什么需要追踪

在 Web 应用中，同一用户可能快速发送多条消息。如果不追踪和取消旧的 Agent，会出现多个 Agent 同时操作同一用户会话的情况，导致消息错乱和资源浪费。

### RegisterAndCancelExisting / Release

```csharp
/// <summary>
/// 取消该用户现有的 Agent（如果有），注册并返回新的 CancellationTokenSource
/// </summary>
public CancellationTokenSource RegisterAndCancelExisting(string userId)

/// <summary>
/// 释放该用户的跟踪记录并 Dispose CTS
/// </summary>
public void Release(string userId, CancellationTokenSource cts)
```

`RegisterAndCancelExisting` 的行为：

1. 创建新的 `CancellationTokenSource`
2. 如果该用户已有正在运行的 Agent，取消旧的 CTS
3. 将新 CTS 注册到追踪字典
4. 返回新 CTS

`Release` 的行为：

1. 只有当字典中存储的 CTS 与传入的是**同一实例**时才移除（防止误删新 Agent 的 CTS）
2. 无论移除是否成功，都 `Dispose` CTS，避免资源泄漏

### 正确的使用模式

```csharp
public async Task HandleMessageAsync(string userId, string userInput)
{
    // 1. 注册新 CTS，自动取消旧 Agent
    var cts = factory.RegisterAndCancelExisting(userId);

    try
    {
        var updates = factory.RunAsync(
            "my-agent", userInput, userId, "feishu_user",
            ct: cts.Token);

        await foreach (var _ in updates) { }
    }
    catch (OperationCanceledException)
    {
        // 用户发来新消息，旧 Agent 被取消，这是正常行为
        logger.LogInformation("Agent 被取消，用户 {UserId}", userId);
    }
    finally
    {
        // 2. 无论如何都释放追踪记录和 CTS
        factory.Release(userId, cts);
    }
}
```

> **注意：** `Release` 必须在 `finally` 中调用。如果跳过，CTS 不会被 Dispose，追踪字典也不会清理，导致内存泄漏。

---

## 作用域生命周期

### Factory 内部管理 Scope

`RunAsync` 每次调用时执行以下流程：

```
1. GetDefinition(agentName)           获取 Agent 定义
2. _scopeFactory.CreateScope()        创建 DI Scope
3. scope.ServiceProvider 解析服务      IUserStorage、AgentContext 等
4. 初始化 AgentContext                赋值 AgentId、ParentId、SystemPrompt 等
5. configure?.Invoke(ctx)             执行调用者回调
6. 构建 Pipeline 并执行               按定义的 PipelineName 查找委托
7. yield return 流式响应              逐条返回 ChatResponseUpdate
8. scope.Dispose()                    finally 中释放 Scope
```

### 子 Agent 独立 Scope

每次 `RunAsync` 调用都创建独立的 Scope。这意味着：

- `AgentContext` 是 Scope 内唯一的，不会被其他 Agent 共享
- `IChatClient` 等 Scoped 服务各自独立
- `EventBus` 是 Singleton 服务，全局共享，但通过 key（`AgentId`）隔离不同 Agent 的事件
- 多个 Agent 可以安全地并发运行

### EventBus 的 key 隔离

`EventBus` 是全局单例（Singleton），通过 key（通常是 `AgentId`）隔离不同 Agent 的事件。订阅时必须使用正确的 key 才能收到对应 Agent 的事件。

```csharp
// ✅ 正确：在 configure 回调中订阅，使用 ctx.AgentId 作为 key
var updates = factory.RunAsync("agent", input, userId, "console", ctx =>
{
    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
    bus.Subscribe<AfterToolExecuteEvent>(ctx.AgentId, async (e, ct) =>
    {
        Console.WriteLine($"工具执行完成: {e.ToolName}");
    });
});

// ❌ 错误：使用错误的 key 订阅，收不到事件
var bus = rootSp.GetRequiredService<EventBus>();
bus.Subscribe<AfterToolExecuteEvent>("wrong-key", ...);  // key 不匹配，收不到事件
```

---

## 完整示例

### Console 应用

```csharp
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// 1. 构建 DI 容器（Agent 定义从 settings.json 的 Agents 字段自动加载）
var services = new ServiceCollection();
services.AddManInBlackFromSettings();

var rootSp = services.BuildServiceProvider();
var factory = rootSp.GetRequiredService<AgentFactory>();

// 2. 准备 EventBus 订阅（变量需在 RunAsync 外声明）
IDisposable? toolExecutingSub = null;
IDisposable? toolExecutedSub = null;
AgentContext? capturedContext = null;

// 3. 运行 Agent
var updates = factory.RunAsync("console-agent", args[0], "console", "Default", ctx =>
{
    capturedContext = ctx;

    // 在 Factory 的 scope 内订阅 EventBus
    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
    var key = ctx.AgentId;
    toolExecutingSub = bus.Subscribe<BeforeToolExecuteEvent>(key, async (@event, ct) =>
    {
        Console.WriteLine($"[Tool Call] {@event.ToolName}({string.Join(", ", @event.Arguments.Select(kv => $"{kv.Key}: {kv.Value}"))})");
    });
    toolExecutedSub = bus.Subscribe<AfterToolExecuteEvent>(key, async (@event, ct) =>
    {
        Console.WriteLine($"[Tool Result] {@event.Result} {@event.Exception}");
    });
});

// 4. 消费流式响应
await foreach (ChatResponseUpdate update in updates)
{
    foreach (var content in update.Contents)
    {
        switch (content)
        {
            case TextContent text:
                Console.Write(text.Text);
                break;
            case UsageContent:
                // usage 由 AgentLoopMiddleware 累积，不显示
                break;
        }
    }
}

// 5. 清理 EventBus 订阅
toolExecutingSub?.Dispose();
toolExecutedSub?.Dispose();

// 6. 输出 Token 用量
var usage = capturedContext?.AccumulatedUsage;
if (usage is not null && (usage.InputTokenCount is not null || usage.OutputTokenCount is not null))
{
    Console.WriteLine($"Token 用量 — 输入: {usage.InputTokenCount}, 输出: {usage.OutputTokenCount}");
}
```

### Web 应用（FeishuAdaptor 模式）

```csharp
// ── Program.cs ──

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddManInBlackSettings();

// 注册核心服务（Agent 定义从 settings.json 的 Agents 字段自动加载）
builder.Services.AddManInBlackFromConfiguration(builder.Configuration);

var app = builder.Build();

// 注册自定义管道（Build 之后才能获取 Factory）
var factory = app.Services.GetRequiredService<AgentFactory>();
factory.RegisterPipeline("feishu", pipeline => pipeline
    .Use<MyCustomMiddleware>()  // 自定义中间件
    .UseDefault());

app.Run();
```

```csharp
// ── AgentLauncher.cs ──

using ManInBlack.AI;
using ManInBlack.AI.Abstraction;

public class AgentLauncher(
    IServiceProvider rootServiceProvider,
    AgentFactory factory,
    ILogger<AgentLauncher> logger)
{
    public async Task LaunchAsync(string userId, string userInput, string openId)
    {
        // 1. 取消旧 Agent，注册新 CTS
        var cts = factory.RegisterAndCancelExisting(userId);

        try
        {
            // 2. 运行 Agent，通过 configure 注入动态信息
            var updates = factory.RunAsync(
                "feishu-agent",
                userInput,
                userId,
                "feishu_user",
                ctx => ctx.SystemPrompt += $"""

                    <system>
                    你的面对的用户的飞书 open id 是: {openId}
                    </system>
                    """,
                cts.Token);

            // 3. 消费响应（实际场景中会发送回飞书）
            await foreach (var _ in updates) { }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Agent 被取消，用户 {UserId}", userId);
        }
        finally
        {
            // 4. 释放追踪记录
            factory.Release(userId, cts);
        }
    }
}
```

---

## 子 Agent 委托

AgentDefinition 的 `SubAgents` 属性声明了该 Agent 可以委托的子 Agent 列表。当 `SubAgents` 非空时，`DelegationMiddleware` 会自动注入委托工具和提示词。

### 配置示例

在 `~/.man-in-black/settings.json` 中配置：

```json
{
  "Agents": {
    "researcher": {
      "Description": "擅长搜索和分析信息",
      "Instruction": "你是一个研究助手...",
      "PipelineName": "simple"
    },
    "orchestrator": {
      "Instruction": "你是一个协调者...",
      "PipelineName": "default",
      "SubAgents": ["researcher"]
    }
  }
}
```

或通过代码注册：

```csharp
// 子 Agent 使用不包含 DelegationMiddleware 的 pipeline（如 "simple"），防止递归委托
services.AddAgentDefinition(new AgentDefinition
{
    Name = "researcher",
    Description = "擅长搜索和分析信息",
    Instruction = "你是一个研究助手...",
    PipelineName = "simple"
});

// 父 Agent 使用 "default" pipeline（包含 DelegationMiddleware）
services.AddAgentDefinition(new AgentDefinition
{
    Name = "orchestrator",
    Instruction = "你是一个协调者...",
    PipelineName = "default",
    SubAgents = ["researcher"]
});
```

### 委托流程

1. 父 Agent 的 `DelegationMiddleware` 检查 `SubAgents` 是否非空
2. 如果非空，注入 `DelegateToAgent` 工具声明和子 Agent 描述到系统提示词
3. LLM 决定调用 `DelegateToAgent(agentName, task)` 工具
4. `DelegationTools` 通过 `AgentFactory.RunAsync` 启动子 Agent
5. 子 Agent 在独立的 DI 作用域和会话中运行
6. 子 Agent 的文本输出作为工具结果返回给父 Agent

### 事件

委托过程会发布以下事件（以父 Agent 的 `AgentId` 为 key）：

- `SubAgentStartedEvent` — 子 Agent 开始执行
- `SubAgentCompletedEvent` — 子 Agent 执行完成（包含结果或错误）

### 注意事项

- **防止递归**：子 Agent 应使用不包含 `DelegationMiddleware` 的 pipeline（如 `simple`），从根本上防止循环委托
- **独立会话**：子 Agent 以 `parentId = 父 AgentId` 创建独立会话，不继承父 Agent 的对话历史
- **CancellationToken 传播**：父 Agent 的取消信号会自动传播到子 Agent

---

## 注意事项

1. **AgentFactory 是 Singleton** — 注册为 `AddSingleton<AgentFactory>()`，整个应用生命周期内只有一个实例。`RunAsync` 每次调用创建独立的 Scope，不会互相干扰。

2. **同名定义会报错** — `RegisterDefinition` 内部使用 `ConcurrentDictionary.TryAdd`，如果同名 Agent 已存在（`TryAdd` 返回 `false`），会抛出 `ArgumentException`。

3. **configure 回调在 Scope 内执行** — 回调中获取的 `ctx.ServiceProvider` 是 Scope 级别的，不是 Root ServiceProvider。用它解析 Scoped 服务是安全的。

4. **EventBus 订阅必须使用正确的 key** — `EventBus` 是 Singleton，通过 key（`AgentId`）隔离事件。在 `configure` 回调中通过 `ctx.ServiceProvider` 获取 EventBus，使用 `ctx.AgentId` 作为 key 订阅。使用错误的 key 会收不到事件。

5. **Release 必须 finally** — 无论 Agent 正常结束还是异常退出，都要调用 `factory.Release(userId, cts)` 来清理追踪记录和释放 CTS。

6. **管道名称必须已注册** — `AgentDefinition.PipelineName` 对应的管道必须在 `RunAsync` 调用前注册，否则抛出 `KeyNotFoundException`。
