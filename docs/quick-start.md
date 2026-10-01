# 快速开始

本文档引导你从零启动一个 ManInBlack Agent。为避免在干净环境中发生隐式环境耦合，**库消费者请从 `simple` 管道起步**，确认基础链路正常后再按需开启能力。

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

## 第二步：安装 NuGet 包

```bash
dotnet add package ManInBlack.AI
```

`[AiTool]` 源生成器已内嵌在包内，无需单独安装。

也可以直接引用本仓库项目（开发模式）：

```bash
dotnet add reference <path>/src/ManInBlack.AI/ManInBlack.AI.csproj
dotnet add reference <path>/src/ManInBlack.AI.SourceGenerator/ManInBlack.AI.SourceGenerator.csproj
```

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
services.AddManInBlack()
    .UseJson();   // 从 ~/.man-in-black/settings.json 读取

// 注册 Agent 定义
services.AddAgentDefinition(new AgentDefinition
{
    Name = "my-agent",
    Instruction = "你是一个有帮助的AI助手。请用中文回复。",
    PipelineName = "simple"   // 从最小管道起步
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

## 按需开启能力（从 simple 到 default）

`simple` 管道仅包含运行 Agent 所需的最小中间件，不会自动读取 `profile.md`、加载 skill、连接 MCP server 或写持久化。按下面顺序按需开启：

### 1. 接入工具

默认 `simple` 管道已包含 `ToolsMiddleware`，因此代码中声明的 `[AiTool]` 工具会自动注入模型。若还不需要工具，保持 `simple` 即可。

```csharp
[ServiceRegister.Scoped]
public partial class WeatherTools
{
    /// <summary>查询指定城市的天气。</summary>
    [AiTool]
    public string GetWeather(string city) => $"{city}: 晴 26°C";
}
```

### 2. 读取 Markdown 角色配置

需要 `AgentProfileMiddleware` 注入 `~/.man-in-black/profile.md` 时，改用 `default` 管道，或自定义管道显式加入该中间件：

```csharp
services.AddAgentDefinition(new AgentDefinition
{
    Name = "my-agent",
    Instruction = "你是一个有帮助的AI助手。",
    PipelineName = "default"
});
```

> 首次使用 `default` 管道时，`AgentProfileMiddleware` 会自动在 `~/.man-in-black/profile.md` 创建模板。

### 3. 加载 Skills

在 `~/.man-in-black/skills/` 下按目录放置 `SKILL.md`，`SkillMiddleware`（`default` 管道）会自动识别并注入提示词。详见 [工具开发指北](./tools-guide.md)。

### 4. 连接 MCP Server

在 `settings.json` 中配置 `McpServers`，应用启动时 `McpClientHostedService` 会自动连接并注册工具。详见 [MCP 工具接入指南](./mcp-guide.md)。

### 5. 持久化历史会话

主包默认使用内存存储，重启后历史会丢失。如需持久化，安装 `ManInBlack.AI.Persistence.Sqlite` 并在 `AddManInBlack()` 后调用 `services.AddManInBlackSqlitePersistence()`，数据将保存在 `~/.man-in-black/maninblack.db`（SQLite）。详见 [存储指南](./storage-guide.md)。

---

## default 管道：产品形态管道

`default` 管道是飞书 bot、Dashboard 等产品形态的默认选择，它在 `simple` 基础上叠加了持久化、Skill、Profile、压缩、命令拦截等能力。**在干净环境中首次使用会产生以下隐式环境耦合**，请务必知晓：

| 中间件 | 触发条件 | 读取/写入的路径 | 外部连接 | 如何关闭或替换 |
| --- | --- | --- | --- | --- |
| `EventPublishingMiddleware` | 始终运行 | 无 | 通过 `EventBus` 发布 `ModelContentEvent` 等事件 | 不使用 `default`；或自定义管道不包含它 |
| `CommandMiddleware` | 用户输入以 `/` 开头 | 无 | 无 | 不使用 `default`；或自定义管道不包含它 |
| `ReadPersistenceMiddleware` | 注册了 `ISessionStorage` 时生效 | 从 `ISessionStorage` 加载历史消息；若实现为 SQLite 则读取 `~/.man-in-black/maninblack.db` | 无 | 不安装 SQLite 包时默认内存存储（重启丢失）；或不使用 `default` |
| `SavePersistenceMiddleware` | 注册了 `ISessionStorage` 时生效 | 新增消息写入 `ISessionStorage`；session 结束保存状态快照 | 无 | 同上 |
| `SkillMiddleware` | 始终运行 | 读取 `~/.man-in-black/skills/` 与当前工作空间 `.agents/skills/` 下的 `SKILL.md` | 无 | 清空 skills 目录；或不使用 `default` |
| `DelegationMiddleware` | `AgentDefinition.SubAgents` 非空 | 读取已注册的 `AgentDefinition` | 无 | 不配置 `SubAgents`；或不使用 `default` |
| `AgentProfileMiddleware` | 始终运行 | 读取 `~/.man-in-black/profile.md`；不存在时**自动创建模板** | 无 | 不使用 `default`；或自定义管道不包含它 |
| `ContextCompressMiddleware` | 始终运行 | 无（仅处理内存消息） | 无 | 不使用 `default`；或自定义管道不包含它 |
| `ToolsMiddleware` | 始终运行 | 无 | 无 | 不使用 `default`；或自定义管道不包含它 |
| 内置工具（`RunBash`、`Read`、`Write` 等） | 模型决定调用时 | 读/写当前工作空间文件；`RunBash` 执行系统命令 | 执行本地/沙盒命令 | 从 `ToolRegistry` 移除、覆盖管道、或要求模型不调用 |
| `HookMiddleware` | 始终运行 | 读取 `~/.man-in-black/hooks/` 与 `{workspace}/.agents/mib-hooks.json` 中配置的钩子 | 执行钩子脚本进程 | `settings.json` 的 `Hooks` 留空；或不使用 `default` |
| `McpClientHostedService` | 应用启动 | 无 | 按 `settings.json` 的 `McpServers` 连接外部 MCP server | 不配置 `McpServers`；或不调用 `AddManInBlack()` 中的 hosted service（高级用法） |

> **持久化说明：** `default` 管道本身不强制写 SQLite。主包 `AddManInBlack()` 已注册内存存储实现，因此不安装 `ManInBlack.AI.Persistence.Sqlite` 时，持久化中间件降级为内存存储，重启后历史丢失。安装 SQLite 包并调用 `services.AddManInBlackSqlitePersistence()` 后才会写入 `~/.man-in-black/maninblack.db`。

> **MCP 连接说明：** `McpClientHostedService` 在应用启动时尝试连接 `settings.json` 中所有 `Enabled=true` 的 MCP server，单个失败只记日志、不阻断启动。模型可见的工具列表会因此变大。

---

## 手动配置（不使用 settings.json）

如果需要在代码中直接配置（不依赖 `settings.json`），使用流式 Builder API：

```csharp
services.AddManInBlack()
    .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx").BaseUrl("https://api.deepseek.com"))
    .AddModelChoice("default", c => c.Provider("default").ModelId("deepseek-chat"))
    .AddAgent("my-agent", a => a
        .Instruction("你是一个AI助手")
        .Pipeline("simple"));   // 从 simple 起步
```

也可以只链入部分配置（如 `.UseJson()` 载入文件后用 `.AddProvider()` 覆盖某个 Provider）。

详见 [配置指南](./configuration-guide.md) 和 [Provider 配置指南](./provider-guide.md)。

---

## 自定义管道

如果 `simple` 太精简、`default` 又太重，可以在 DI 期注册自定义管道：

```csharp
services.AddManInBlack()
    .UseJson()
    .AddPipeline("my-pipeline", builder => builder
        .Use<AgentProfileMiddleware>()   // 只加需要的中间件
        .UseSimple());                   // 必须以 UseSimple 结尾
```

> **运行时动态注册：** 如果需要在 DI 容器构建之后才确定管道配置，可以使用 `AgentFactory.RegisterPipeline()`（见 [Agent 工厂指南](./agent-factory-guide.md)）。

> **管道顺序规则：** `AgentLoopMiddleware` 必须始终是最内层（最后注册）中间件。修改 `SystemPrompt` 的中间件必须在 `SystemPromptInjectionMiddleware` 之前。详见 [中间件开发指北](./middleware-guide.md)。

---

## 进阶：加载历史会话

`AgentFactory` 内部自动管理会话生命周期。`SessionId` 由 `IUserStorage` 自动解析，无需手动设置。`default` 管道中的 `ReadPersistenceMiddleware` 会自动从 `ISessionStorage` 恢复历史消息。

主包默认使用内存存储，重启后历史会丢失。如需持久化，安装 `ManInBlack.AI.Persistence.Sqlite` 并在 `AddManInBlack()` 后调用 `services.AddManInBlackSqlitePersistence()`，数据将保存在 `~/.man-in-black/maninblack.db`（SQLite）。详见 [存储指南](./storage-guide.md)。

---

## 下一步

- 查看 [Agent 工厂指南](./agent-factory-guide.md) 了解 Agent 定义、管道注册和完整生命周期管理
- 查看 [配置指南](./configuration-guide.md) 了解配置系统、IOptions 和文件变更跟踪
- 了解 [架构概览](./architecture.md) 理解洋葱模型
- 查看 [Middleware 开发指北](./middleware-guide.md) 学习编写自定义中间件
- 阅读 [中间件测试指北](./testing-guide.md) 了解测试方法论
- 参考 [Provider 配置指南](./provider-guide.md) 完成所有提供商配置
- 想一条命令同时启动飞书 bot + Dashboard + 前端?见 [Aspire 编排指南](./aspire-guide.md)
