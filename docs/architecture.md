# 架构概览

本文档介绍 ManInBlack 的整体架构、项目分层和关键设计决策。

---

## 一句话总结

ManInBlack 是一个 .NET AI 代理框架，通过**洋葱模型中间件管道**为 15 个 AI 提供商提供统一抽象，支持工具调用、持久化、上下文压缩、重试等能力。

---

## 项目分层

```
┌───────────────────────────────────────────────────┐
│  demo/AgentConsole  demo/FeishuAdaptor  ...       │  ← 应用层
├───────────────────────────────────────────────────┤
│  ManInBlack.AI         中间件实现 + 工具 + 服务   │  ← 实现层
│  ManInBlack.AI.Core    IChatClient 适配器 + 抽象  │  ← 抽象层
│  ManInBlack.AI.SG      四个增量源生成器           │  ← 编译层
└───────────────────────────────────────────────────┘
```

### ManInBlack.AI.Core — 抽象层

提供整个框架的基础接口和抽象类：

| 目录          | 职责                                               |
| ------------- | -------------------------------------------------- |
| `ChatClient/` | 3 个 IChatClient 适配器（OpenAI/Anthropic/Gemini） |
| `Middleware/` | AgentMiddleware、AgentContext、PipelineBuilder     |
| `Storage/`    | ISessionStorage、IUserStorage                      |
| `Tools/`      | IToolExecutor、ToolExecuteContext、ToolCallFilter  |
| `Attributes/` | [AiTool]、[ServiceRegister]（驱动源生成器）        |

### ManInBlack.AI — 实现层

所有中间件实现 + 工具 + 服务：

| 目录               | 内容                                                 |
| ------------------ | ---------------------------------------------------- |
| `Middlewares/`     | 12 个中间件（Logging 到 AgentLoop）                  |
| `Tools/`           | CommandLineTools、FileTools、SkillTools              |
| `ToolCallFilters/` | LoggingFilter、BroadCastingFilter、LargeResultFilter |
| `Services/`        | SkillService、EventBus、FileUserWorkspace 等         |

### ManInBlack.AI.SourceGenerator — 编译层

四个增量源生成器，在编译期扫描属性并生成样板代码：

| 生成器                       | 产出                                   |
| ---------------------------- | -------------------------------------- |
| ToolCallerGenerator          | `ToolExecutor : IToolExecutor` 实现类  |
| ToolDeclarationGenerator     | JSON Schema 工具声明 + MIB010-013 诊断 |
| ServiceRegistrationGenerator | `AddAutoRegisteredServices()` DI 扩展  |
| ToolMiddlewareGenerator      | 每个工具类的 `XxxMiddleware` + DI 注册 |

---

## 洋葱模型管道

### 核心概念

中间件管道是框架最重要的抽象。底层模型复用 ASP.NET Core 的设计思想：

```csharp
public abstract class AgentMiddleware
{
    public abstract IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        CancellationToken ct = default
    );
}
```

- **`AgentContext`** — 请求上下文，携带 Messages、Options、SystemPrompt 等所有状态
- **`ChatResponseUpdateHandler`** — `Func<IAsyncEnumerable<ChatResponseUpdate>>`，代表管道下游
- **`HandleAsync`** — 中间件的核心方法，决定何时（是否）调用 `next()`

### 构建规则

`AgentPipelineBuilder.Build()` 以**逆序**包裹中间件，最终终点是 `IChatClient.GetStreamingResponseAsync()`：

```csharp
builder.Use<A>().Use<B>().Use<C>().Build(sp);

// 内部包装顺序：A → B → C → IChatClient
// 执行方向：A.前置 → B.前置 → C.前置 → ChatClient → C.后置 → B.后置 → A.后置
```

### 默认管道

通过 `builder.UseDefault()` 配置的完整管道：

```
ReadPersistence → SavePersistence → Skill → AgentProfile
    → ContextCompress → CommandLineTools → FileTools → [UseSimple]

[UseSimple]
Logging → MessageEnrich → SystemPromptInjection → UserInput → Retry → AgentLoop
```

注册顺序 = 从外到内的到达顺序。AgentLoop 必须在最内层（最后注册）。

### Context 数据流

```
UserInput ──→ SystemPrompt ──→ Messages ──→ Options ──→ IChatClient
                                                    ↓
User ←──────────────── ChatResponseUpdate 流 ←────────┘
             ↑                    ↑
         Retry 拦截           AgentLoop 循环
```

---

## ChatClient 适配器层

### 三态适配

15 个提供商最终通过 `CompatibleWith` 字段映射到 3 种 API 协议：

```
CompatibleWith: "OpenAI"   → OpenAICompatibleChatClient   (SSE: data: ... [DONE])
CompatibleWith: "Anthropic" → AnthropicCompatibleChatClient (SSE: content_block_start/delta/stop)
CompatibleWith: "Gemini"   → GeminiCompatibleChatClient     (SSE + API Key in query param)
```

### 提供商注册表

| CompatibleWith | 提供商                                                                             |
| -------------- | ---------------------------------------------------------------------------------- |
| OpenAI         | OpenAI, Kimi, DeepSeek, Qwen, Zhipu, Yi, Baichuan, StepFun, Spark, Doubao, MiniMax |
| Anthropic      | Anthropic                                                                          |
| Gemini         | Gemini                                                                             |

### 工厂分发

`ChatClientProviderExtensions.CreateChatClient()` 通过 `switch(CompatibleWith)` 创建对应的适配器实例，注入 `HttpClient`
和认证头。

---

## 源生成器

### 为什么需要源生成器

手写工具分发代码需要：

- 为每个 `[AiTool]` 方法生成 AIFunctionDeclaration + JSON Schema
- 在 IToolExecutor 中 dispatcher switch 路由到对应方法
- 组装工具声明到 ChatOptions.Tools

源生成器在编译期完成所有这些，减少反射开销和手动维护。

### 诊断规则

| ID     | Severity | 触发条件                                 |
| ------ | -------- | ---------------------------------------- |
| MIB001 | Error    | `[ServiceRegister.X.As<T>]` 类型不实现 T |
| MIB010 | Error    | 含 `[AiTool]` 方法的类不是 `partial`     |
| MIB011 | Warning  | `[AiTool]` 方法缺少 `<summary>`          |
| MIB012 | Warning  | `[AiTool]` 参数缺少 `<param>`            |
| MIB013 | Warning  | 非 void `[AiTool]` 缺少 `<returns>`      |

---

## DI 注册流程

```
AddManInBlackCore(configure)
    ├── AgentPipelineBuilder        (Scoped)
    ├── AgentContext                (Scoped)
    ├── AgentExecutionTracker       (Singleton)
    ├── IChatClient                 (Singleton, via CreateChatClient)
    └── HttpClient                  (PooledConnectionLifetime: 2min)

AddAutoRegisteredServices()         [源生成]
    └── 扫描 [ServiceRegister.*] → 注册到 DI

AddToolExecutor()                   [源生成]
    └── 注册 ToolExecutor : IToolExecutor

AddToolMiddlewares()                [源生成]
    └── 注册每个工具类的 XxxMiddleware
```

---

## 工具调用流程

```
用户输入 "read file"
        │
        ▼
┌─────────────────────┐
│  管道前置中间件       │  加载历史、注入 prompt、添加 tool 声明
└─────────────────────┘
        │
        ▼
┌─────────────────────┐
│  IChatClient         │  请求 API，streaming 返回
└─────────────────────┘
        │
        ▼ content_block: FunctionCallContent("ReadFile", {path:"test.txt"})
┌─────────────────────┐
│  AgentLoopMiddleware │  收集 functionCalls → 调用 IToolExecutor
└─────────────────────┘
        │
        ▼ ToolCallFilter 管道 (Logging → BroadCasting → LargeResult)
┌─────────────────────┐
│  ToolExecutor        │  switch(name) → 调用对应的 [AiTool] 方法
└─────────────────────┘
        │
        ▼ FunctionResultContent
┌─────────────────────┐
│  AgentLoopMiddleware │  追加 tool 结果到 Messages → 再次调用 IChatClient
└─────────────────────┘
        │
        ▼ TextContent("Done!")
```

---

## 关键设计决策

1. **Fake it till you make it** — 依赖接口（ISessionStorage、IToolExecutor），不依赖实现。这让测试用 Fake 实现成为可能。

2. **静态诊断优于运行时错误** — MIB010-MIB013 在编译期捕获问题，而不等到运行时。

3. **源生成器使用 EasyCodeBuilder** — 不用原始 StringBuilder，保证代码产出格式规范、可读。

4. **Scoped 生命周期** — 所有中间件注册为 Scoped，每次请求独立，共享同一作用域内的 AgentContext 和 EventBus。
