# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build ManInBlack.slnx                          # Build entire solution
dotnet build src/ManInBlack.AI.Abstraction             # Abstraction library (interfaces + base types)
dotnet build src/ManInBlack.AI                         # Main library (implementations + DI)
dotnet build src/ManInBlack.AI.SourceGenerator         # Source generator
dotnet run --project demo/AgentConsole                 # Agent console demo
dotnet run --project demo/Playground                   # M.E.AI type explorer
dotnet run --project demo/FeishuAdaptor                # 飞书 bot demo
dotnet test test/ManInBlack.AI.Tests                   # All tests (xunit)
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~OpenAI"  # Specific tests
```

No linting or formatting tools are configured.

## Project Overview

.NET 10 library providing unified abstractions for multiple AI chat providers (OpenAI, Anthropic, Gemini, DeepSeek,
Qwen, Kimi, Zhipu, Yi, Baichuan, StepFun, Spark, Doubao, MiniMax) through `Microsoft.Extensions.AI`'s `IChatClient`
interface.

## Architecture

`src/` · `demo/` · `test/`

### Project structure

- **`ManInBlack.AI.Abstraction`** — Interfaces, abstract classes, POCOs, attributes: `IModelProvider`/`ModelProvider`,
  `AgentMiddleware`/`AgentContext`, `ISessionStorage`/`IUserStorage`, `IToolExecutor`, `[AiTool]`/`[ServiceRegister]`.
- **`ManInBlack.AI`** — All concrete implementations: ChatClient adapters, Provider subclasses, DI registration,
  Configuration loading, middlewares, tools, services. References Abstraction.
- **`ManInBlack.AI.SourceGenerator`** — Incremental generators (.NET Standard 2.0).

### ChatClient layer

- **`IModelProvider`** — Provider definitions with `CompatibleWith` dispatching to API shape: `"OpenAI"`, `"Anthropic"`,
  or `"Gemini"`. Most Chinese providers are OpenAI-compatible.
- **Three adapters** — `OpenAICompatibleChatClient` (SSE), `AnthropicCompatibleChatClient` (content_block events),
  `GeminiCompatibleChatClient` (query-param auth). All handle streaming + non-streaming + tool calling.
- **`ChatClientProviderExtensions.CreateChatClient()`** — Factory dispatching by `CompatibleWith`.

### Configuration

- **`~/.man-in-black/settings.json`** — Unified config file. Auto-created on first run.
- **`SettingsLoader`** — Loads settings and maps `Provider` name to concrete `ModelProvider` subclass.
- **`AddManInBlackFromSettings()`** — DI extension that reads from settings file.

### Middleware pipeline

- **`AgentMiddleware`** — `HandleAsync(AgentContext, ChatResponseUpdateHandler, CancellationToken)` calling `next()` to
  chain.
- **`AgentPipelineBuilder`** — `Use()` / `Use<TMiddleware>()` registration, `Build(IServiceProvider)` wraps middlewares
  in reverse around terminal `IChatClient`.
- **`AgentContext`** — Carries `Messages`, `Options`, `SystemPrompt`, `UserInput`, `Items`, `CancellationToken`,
  `IServiceProvider`.

### Source generators

| Generator                      | Purpose                                                                            |
|--------------------------------|------------------------------------------------------------------------------------|
| `ToolCallerGenerator`          | Dispatches `[AiTool]` method calls via `IServiceProvider`                          |
| `ToolDeclarationGenerator`     | Generates `AIFunctionDeclaration` + JSON Schema from `[AiTool]` methods + XML docs |
| `ServiceRegistrationGenerator` | DI registration for `[ServiceRegister]`-attributed classes                         |

All emitters use **Fengb3.EasyCodeBuilder** (`Code.Create().Using(...).Namespace(ns => ...)` /
`Code.Build(option, new CodeBuilder())`).

### Diagnostic rules

| ID     | Severity | Trigger                                                    |
|--------|----------|------------------------------------------------------------|
| MIB001 | Error    | `[ServiceRegister.X.As<T>]` where type doesn't implement T |
| MIB010 | Error    | Class with `[AiTool]` methods is not `partial`             |
| MIB011 | Warning  | `[AiTool]` method missing `<summary>`                      |
| MIB012 | Warning  | `[AiTool]` parameter missing `<param>`                     |
| MIB013 | Warning  | Non-void `[AiTool]` missing `<returns>`                    |

### Feishu Adaptor (`demo/FeishuAdaptor`)

飞书 IM bot via WebSocket + streaming cards. Flow: `FeishuCardMiddleware` maps content types to ViewModels →
`CardView<T>.BindMarkdown()` wires `PropertyChanged` → `CardUpdateScheduler` (singleton, 50/sec 1000/min rate limit)
batches updates to Feishu API. Cards use JSON 2.0 with snake_case serialization.

## Code Style

- Comments and XML docs in Chinese
- C# file-scoped namespaces, implicit usings, nullable enabled
- C# extension syntax for DI (`extension(IServiceCollection services)`)
- Source generator emitters must use EasyCodeBuilder, not raw StringBuilder
- IDE: JetBrains Rider


## Documentations

- 架构概览: [architecture](docs/architecture.md)
- 配置指南: [configuration-guide](docs/configuration-guide.md)
- Provider 配置指南: [provider-guide](docs/provider-guide.md)
- 快速开始: [quick-start](docs/quick-start.md)
- 中间件开发指北: [middleware-guide](docs/middleware-guide.md)
- 中间件测试指北: [testing-guide](docs/testing-guide.md)
