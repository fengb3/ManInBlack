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
  "Provider": "OpenAI",
  "ApiKey": "sk-xxxxxxxx",
  "BaseUrl": "https://api.openai.com",
  "ModelId": "gpt-4o"
}
```

对中国厂商（如 DeepSeek）：

```json
{
  "Provider": "DeepSeek",
  "ApiKey": "sk-xxxxxxxx",
  "BaseUrl": "https://api.deepseek.com",
  "ModelId": "deepseek-chat"
}
```

`BaseUrl` 可选，每个 Provider 有默认值。完整配置说明见 [配置指南](./configuration-guide.md)。

---

## 第四步：编写代码

```csharp
using ManInBlack.AI;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// 构建 DI 容器（从 ~/.man-in-black/settings.json 读取配置）
var services = new ServiceCollection();
services.AddManInBlackFromSettings();

// 配置 Agent
var rootSp = services.BuildServiceProvider();
using var scope = rootSp.CreateScope();
var sp = scope.ServiceProvider;

var ctx = sp.GetRequiredService<AgentContext>();
ctx.AgentId    = Guid.NewGuid().ToString();
ctx.ParentId   = "my-user";
ctx.ParentType = "User";

// 构建管道
var pipeline = new AgentPipelineBuilder()
    .UseDefault()
    .Build(sp);

// 发起对话
ctx.SystemPrompt = "你是一个有帮助的AI助手。请用中文回复。";
ctx.UserInput    = "帮我解释一下什么是依赖注入";

// 流式输出
await foreach (var update in pipeline(ctx))
{
    foreach (var content in update.Contents)
    {
        switch (content)
        {
            case TextContent text:
                Console.Write(text.Text);
                break;
            case TextReasoningContent reasoning:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(reasoning.Text);
                Console.ResetColor();
                break;
            // UsageContent 由 AgentLoopMiddleware 自动累积
        }
    }
}

// 查看用量
var usage = ctx.AccumulatedUsage;
Console.WriteLine($"\nToken 用量 — 输入: {usage.InputTokenCount}, 输出: {usage.OutputTokenCount}");
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

如果需要在代码中直接配置 Provider，使用 `AddManInBlack`：

```csharp
services.AddManInBlack(opt =>
{
    opt.ModelChoice = new ModelChoice
    {
        Provider = new DeepSeekProvider()
        {
            ApiKey = "sk-xxx",
            BaseUrl = "https://api.deepseek.com",
        },
        ModelId = "deepseek-chat",
    };
});
```

所有 Provider 类在 `ManInBlack.AI` 命名空间下。详见 [Provider 配置指南](./provider-guide.md)。

---

## 使用最小管道

`UseDefault()` 包含完整管道（持久化、Skill、压缩等）。如果只需要最小管道：

```csharp
var pipeline = new AgentPipelineBuilder()
    .UseSimple()  // Logging → Enrich → SystemPrompt → UserInput → Retry → AgentLoop
    .Build(sp);
```

`UseSimple()` 不包含持久化和压缩，更适合一次性对话。

---

## 进阶：加载历史会话

管道中 `ReadPersistenceMiddleware` 自动从 `ISessionStorage` 恢复历史消息。你只需要设置正确的 `SessionId` 和 `ParentId`：

```csharp
ctx.SessionId = "my-user_1713456789";  // 指定会话 ID，从该会话恢复
ctx.ParentId  = "my-user";
```

持久化基于实现了 `IUserStorage` 的服务。默认实现 `FileUserStorage` 将数据保存在 `~/.man-in-black/`。

---

## 下一步

- 查看 [配置指南](./configuration-guide.md) 了解配置系统、IOptions 和文件变更跟踪
- 了解 [架构概览](./architecture.md) 理解洋葱模型
- 查看 [Middleware 开发指北](./middleware-guide.md) 学习编写自定义中间件
- 阅读 [中间件测试指北](./testing-guide.md) 了解测试方法论
- 参考 [Provider 配置指南](./provider-guide.md) 完成所有提供商配置
