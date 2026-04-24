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

## 第二步：安装依赖

```bash
dotnet add package DotNetEnv          # .env 文件读取
dotnet add reference <path>/src/ManInBlack.AI/ManInBlack.AI.csproj
```

> 项目当前为本地引用模式。NuGet 包模式待后续发布。

---

## 第三步：配置 .env

在项目根目录（生成输出目录）创建 `.env`：

```env
OPENAI_API_KEY=sk-xxxxxxxx
OPENAI_BASE_URL=https://api.openai.com
OPENAI_MODEL_ID=gpt-4o
```

对中国厂商（如 DeepSeek）：

```env
DEEPSEEK_API_KEY=sk-xxxxxxxx
DEEPSEEK_BASE_URL=https://api.deepseek.com
DEEPSEEK_MODEL_ID=deepseek-chat
```

---

## 第四步：编写代码

```csharp
using DotNetEnv;
using ManInBlack.AI;
using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// 1. 加载 .env 配置
Env.Load();

// 2. 构建 DI 容器
var services = new ServiceCollection();
services.AddManInBlackCore(opt =>
{
    opt.ModelChoice = new ModelChoice
    {
        Provider = new OpenAIProvider()
        {
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
            BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "",
        },
        ModelId = Environment.GetEnvironmentVariable("OPENAI_MODEL_ID") ?? "",
    };
});

// 3. 配置 Agent
var rootSp = services.BuildServiceProvider();
using var scope = rootSp.CreateScope();
var sp = scope.ServiceProvider;

var ctx = sp.GetRequiredService<AgentContext>();
ctx.AgentId    = Guid.NewGuid().ToString();
ctx.ParentId   = "my-user";
ctx.ParentType = "User";

// 4. 构建管道
var pipeline = new AgentPipelineBuilder()
    .UseDefault()
    .Build(sp);

// 5. 发起对话
ctx.SystemPrompt = "你是一个有帮助的AI助手。请用中文回复。";
ctx.UserInput    = "帮我解释一下什么是依赖注入";

// 6. 流式输出
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

// 7. 查看用量
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

## 使用其他提供商

将 `OpenAIProvider()` 替换为任意 15 个提供商：

```csharp
// DeepSeek
Provider = new DeepSeekProvider() { ApiKey = "...", BaseUrl = "..." },
ModelId = "deepseek-chat",

// 通义千问
Provider = new QwenProvider() { ApiKey = "...", BaseUrl = "..." },
ModelId = "qwen-max",

// Anthropic Claude
Provider = new AnthropicProvider() { ApiKey = "...", BaseUrl = "..." },
ModelId = "claude-sonnet-4-5-20250929",
```

详见 [Provider 配置指南](./provider-guide.md)。

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

- 了解 [架构概览](./architecture.md) 理解洋葱模型
- 查看 [Middleware 开发指北](./middleware-guide.md) 学习编写自定义中间件
- 阅读 [中间件测试指北](./testing-guide.md) 了解测试方法论
- 参考 [Provider 配置指南](./provider-guide.md) 完成所有提供商配置
