# 快速开始

本文档引导你从零启动一个 ManInBlack Agent。

---

## 前置条件

- **.NET 10 SDK** 或更高版本
- 至少一个 AI 提供商的 **API Key**（见 [Provider 配置指南](./provider-guide.md)）

---

## 第一步：创建项目

```bash
dotnet new console -n MyAgent
cd MyAgent
```

---

## 第二步：添加项目引用

```bash
dotnet add reference <path>/src/ManInBlack.AI/ManInBlack.AI.csproj
dotnet add reference <path>/src/ManInBlack.AI.SourceGenerator/ManInBlack.AI.SourceGenerator.csproj
```

> 项目当前为本地引用模式。NuGet 包模式待后续发布。

---

## 第三步：配置 settings.json

首次运行时会自动在 `~/.man-in-black/` 下创建 `settings.json`，填入实际值即可：

```json
{
  "Providers": {
    "default": {
      "Schema": "OpenAI",
      "ApiKey": "sk-xxxxxxxx"
    }
  },
  "ModelChoices": {
    "default": {
      "ProviderName": "default",
      "ModelId": "gpt-4o"
    }
  }
}
```

使用 DeepSeek 等其他厂商时，只需改 `BaseUrl`：

```json
{
  "Providers": {
    "default": {
      "Schema": "OpenAI",
      "ApiKey": "sk-xxxxxxxx",
      "BaseUrl": "https://api.deepseek.com"
    }
  },
  "ModelChoices": {
    "default": {
      "ProviderName": "default",
      "ModelId": "deepseek-chat"
    }
  }
}
```

`BaseUrl` 可选，不填则使用 Schema 对应的默认值。完整配置说明见 [配置指南](./configuration-guide.md)。

---

## 第四步：编写代码

```csharp
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.DependencyInjection;

// 构建 DI 容器（从 ~/.man-in-black/settings.json 读取配置）
var services = new ServiceCollection();
services.AddManInBlackFromSettings();

// 注册 Agent 定义
services.AddAgentDefinition(new AgentDefinition
{
    Name = "my-agent",
    Instruction = "你是一个有帮助的AI助手。请用中文回复。",
    PipelineName = "default"
});

var rootSp = services.BuildServiceProvider();

// 通过 AgentFactory 运行 agent
var factory = rootSp.GetRequiredService<AgentFactory>();
AgentContext? capturedContext = null;
var subs = new List<IDisposable>();

var updates = factory.RunAsync("my-agent", "帮我解释一下什么是依赖注入", "my-user", "User", ctx =>
{
    capturedContext = ctx;

    // 在 Factory 的 scope 内订阅 EventBus，用 AgentId 作为 key 隔离事件
    var key = ctx.AgentId;
    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();

    // 订阅模型流式输出（推荐方式）
    var last = "";
    subs.Add(bus.Subscribe<ModelContentEvent>(key, async (evt, ct) =>
    {
        switch (evt.Kind)
        {
            case ModelContentKind.Reasoning:
                if (last != "reasoning")
                    Console.WriteLine("[Reasoning]");
                last = "reasoning";
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(evt.Text);
                Console.ResetColor();
                break;
            case ModelContentKind.Text:
                if (last != "text")
                    Console.WriteLine();
                last = "text";
                Console.Write(evt.Text);
                break;
        }
    }));

    // 订阅工具调用过程
    subs.Add(bus.Subscribe<BeforeToolExecuteEvent>(key, async (@event, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[Tool Call] {@event.ToolName}({@event.ArgumentsJson})");
        Console.ResetColor();
    }));
    subs.Add(bus.Subscribe<AfterToolExecuteEvent>(key, async (@event, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Tool Result] {@event.ResultJson} {@event.Error}");
        Console.ResetColor();
    }));
});

// 仅驱动枚举，输出由上面的 EventBus handler 处理
await foreach (var _ in updates) { }

// 清理 EventBus 订阅
foreach (var sub in subs) sub.Dispose();

// 查看用量
Console.WriteLine();
var usage = capturedContext?.AccumulatedUsage;
if (usage is not null && (usage.InputTokenCount is not null || usage.OutputTokenCount is not null))
    Console.WriteLine($"Token 用量 — 输入: {usage.InputTokenCount}, 输出: {usage.OutputTokenCount}");
```

---

## 第五步：运行

```bash
dotnet run
```

预期输出：

```
=== 依赖注入是一种设计模式...

它允许对象从外部获取其依赖，而不是在内部创建。在 .NET 中...
...
Token 用量 — 输入: 42, 输出: 128
```

---

## 手动配置（不使用 settings.json）

如果需要在代码中直接配置（不依赖 `settings.json`），使用 `AddManInBlack`：

```csharp
services.AddManInBlack(opt =>
{
    // ModelChoice 是运行时使用的模型选择对象，包含协议、密钥、地址和模型 ID
    opt.ModelChoice = new ModelChoice
    {
        Schema = "OpenAI",
        ApiKey = "sk-xxx",
        BaseUrl = "https://api.deepseek.com",
        ModelId = "deepseek-chat",
    };
});
```

> **注意**：`ModelChoice` 是代码中使用的运行时类型，而 `settings.json` 中使用的是 `ModelChoiceSettings`（仅包含 `ProviderName` + `ModelId`，通过引用 `Providers` 字典中的条目间接获取密钥和地址）。两者不可混用——`AddManInBlack` 接受 `ModelChoice`，`AddManInBlackFromSettings` 内部会将 `ModelChoiceSettings` + `ProviderSettings` 解析为 `ModelChoice`。

详见 [Provider 配置指南](./provider-guide.md)。

---

## 使用最小管道

`UseDefault()` 包含完整管道（持久化、Skill、压缩等）。如果只需要最小管道，可以在 `AgentDefinition` 中指定 `PipelineName = "simple"`：

```csharp
services.AddAgentDefinition(new AgentDefinition
{
    Name = "simple-agent",
    Instruction = "你是一个AI助手",
    PipelineName = "simple"  // Logging → Enrich → SystemPrompt → UserInput → Retry → AgentLoop
});
```

`simple` 管道不包含持久化和压缩，更适合一次性对话。

也可以注册自定义管道：

```csharp
// 在 DI 容器构建完成后，从 ServiceProvider 获取 AgentFactory
var sp = services.BuildServiceProvider();
var factory = sp.GetRequiredService<AgentFactory>();

// RegisterPipeline 需要在 RunAsync 之前调用
factory.RegisterPipeline("my-pipeline", builder => builder
    .Use<MyCustomMiddleware>()
    .UseSimple());
```

---

## 进阶：加载历史会话

`AgentFactory` 内部自动管理会话生命周期。`SessionId` 由 `IUserStorage` 自动解析，无需手动设置。`default` 管道中的 `ReadPersistenceMiddleware` 会自动从 `ISessionStorage` 恢复历史消息。

持久化基于实现了 `IUserStorage` 的服务。默认实现 `FileUserStorage` 将数据保存在 `~/.man-in-black/`。

---

## 下一步

- 查看 [Agent 工厂指南](./agent-factory-guide.md) 了解 Agent 定义、管道注册和完整生命周期管理
- 查看 [配置指南](./configuration-guide.md) 了解配置系统、IOptions 和文件变更跟踪
- 了解 [架构概览](./architecture.md) 理解洋葱模型
- 查看 [Middleware 开发指北](./middleware-guide.md) 学习编写自定义中间件
- 阅读 [中间件测试指北](./testing-guide.md) 了解测试方法论
- 参考 [Provider 配置指南](./provider-guide.md) 完成所有提供商配置
