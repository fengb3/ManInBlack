# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build ManInBlack.slnx                          # Build entire solution
dotnet build src/ManInBlack.AI.Core                   # Core library (adapters + base types)
dotnet build src/ManInBlack.AI                        # Main library (middleware implementations)
dotnet build src/ManInBlack.AI.SourceGenerator        # Source generator
dotnet run --project demo/AgentConsole                # Agent console demo
dotnet run --project demo/Playground                  # M.E.AI type explorer
dotnet run --project demo/FeishuAdaptor               # 飞书 bot demo
dotnet test test/ManInBlack.AI.Tests                  # All tests (xunit)
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~OpenAI"  # Specific tests
```

No linting or formatting tools are configured.

## Project Overview

.NET 10 library providing unified abstractions for multiple AI chat providers (OpenAI, Anthropic, Gemini, DeepSeek, Qwen, Kimi, Zhipu, Yi, Baichuan, StepFun, Spark, Doubao, MiniMax) through `Microsoft.Extensions.AI`'s `IChatClient` interface.

## Architecture

`src/` · `demo/` · `test/`

### Project structure

- **`ManInBlack.AI.Core`** — Base abstractions: `IChatClient` adapters, `AgentMiddleware`/`AgentContext`/`AgentPipelineBuilder`, `[AiTool]`/`[ServiceRegister]` attributes, `ToolFunctionDeclaration`.
- **`ManInBlack.AI`** — Concrete middlewares (Logging, MessageEnrich, SystemPromptInjection, Persistence, ContextCompress, CommandTool, Skill, AgentLoop). References Core.
- **`ManInBlack.AI.SourceGenerator`** — Incremental generators (.NET Standard 2.0).

### ChatClient layer

- **`IModelProvider`** — Provider definitions with `CompatibleWith` dispatching to API shape: `"OpenAI"`, `"Anthropic"`, or `"Gemini"`. Most Chinese providers are OpenAI-compatible.
- **Three adapters** — `OpenAICompatibleChatClient` (SSE), `AnthropicCompatibleChatClient` (content_block events), `GeminiCompatibleChatClient` (query-param auth). All handle streaming + non-streaming + tool calling.
- **`ChatClientProviderExtensions.CreateChatClient()`** — Factory dispatching by `CompatibleWith`.

### Middleware pipeline

- **`AgentMiddleware`** — `HandleAsync(AgentContext, ChatResponseUpdateHandler, CancellationToken)` calling `next()` to chain.
- **`AgentPipelineBuilder`** — `Use()` / `Use<TMiddleware>()` registration, `Build(IServiceProvider)` wraps middlewares in reverse around terminal `IChatClient`.
- **`AgentContext`** — Carries `Messages`, `Options`, `SystemPrompt`, `UserInput`, `Items`, `CancellationToken`, `IServiceProvider`.

### Source generators

| Generator | Purpose |
|-----------|---------|
| `ToolCallerGenerator` | Dispatches `[AiTool]` method calls via `IServiceProvider` |
| `ToolDeclarationGenerator` | Generates `AIFunctionDeclaration` + JSON Schema from `[AiTool]` methods + XML docs |
| `ServiceRegistrationGenerator` | DI registration for `[ServiceRegister]`-attributed classes |

All emitters use **Fengb3.EasyCodeBuilder** (`Code.Create().Using(...).Namespace(ns => ...)` / `Code.Build(option, new CodeBuilder())`).

### Diagnostic rules

| ID | Severity | Trigger |
|----|----------|---------|
| MIB001 | Error | `[ServiceRegister.X.As<T>]` where type doesn't implement T |
| MIB010 | Error | Class with `[AiTool]` methods is not `partial` |
| MIB011 | Warning | `[AiTool]` method missing `<summary>` |
| MIB012 | Warning | `[AiTool]` parameter missing `<param>` |
| MIB013 | Warning | Non-void `[AiTool]` missing `<returns>` |

### Feishu Adaptor (`demo/FeishuAdaptor`)

飞书 IM bot via WebSocket + streaming cards. Flow: `FeishuCardMiddleware` maps content types to ViewModels → `CardView<T>.BindMarkdown()` wires `PropertyChanged` → `CardUpdateScheduler` (singleton, 50/sec 1000/min rate limit) batches updates to Feishu API. Cards use JSON 2.0 with snake_case serialization.

## Code Style

- Comments and XML docs in Chinese
- C# file-scoped namespaces, implicit usings, nullable enabled
- C# extension syntax for DI (`extension(IServiceCollection services)`)
- Source generator emitters must use EasyCodeBuilder, not raw StringBuilder
- IDE: JetBrains Rider
