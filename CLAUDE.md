# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build ManInBlack.slnx           # Build the solution
dotnet build ManInBlack.AI             # Build just the library
dotnet run --project Playground        # Run the playground (explores M.E.AI types)
```

No test project exists yet. No linting or formatting tools are configured.

## Project Overview

ManInBlack is a .NET 10 library that provides unified abstractions for accessing multiple AI chat model providers through `Microsoft.Extensions.AI`'s `IChatClient` interface. It acts as an adapter layer supporting OpenAI, Anthropic, Gemini, and numerous Chinese AI providers (DeepSeek, Qwen, Kimi, Zhipu, Yi, Baichuan, StepFun, Spark, Doubao, MiniMax).

## Architecture

The core pattern is **Provider + Adapter**:

- **`IModelProvider` / `ModelProvider`** (`IModelProvider.cs`) — Abstract provider definitions. Each provider declares a `ProviderName`, `BaseUrl`, `ApiKey`, and `CompatibleWith` (the API shape it follows: `"OpenAI"`, `"Anthropic"`, or `"Gemini"`). Most Chinese providers are OpenAI-compatible.
- **ChatClient adapters** (`ChatClient/`) — Three `IChatClient` implementations, one per API shape:
  - `OpenAICompatibleChatClient` — POST to `v1/chat/completions`, SSE streaming, tool call fragment accumulation
  - `AnthropicCompatibleChatClient` — POST to `v1/messages`, separate `content_block_start/delta/stop` SSE events, system prompt extracted from messages
  - `GeminiCompatibleChatClient` — POST to `v1beta/models/{model}:generateContent` (and `:streamGenerateContent`), API key in URL query param
- **`ChatClientProviderExtensions.CreateChatClient()`** (`IModelProvider.cs`) — Factory method that dispatches to the correct adapter based on `CompatibleWith`
- **`ModelChoice`** — Couples a `ModelProvider` with a `ModelId` for client creation
- **`ModelProviderRegistry`** — Named provider registry (currently minimal, no lookup API yet)

### Key design points

- All adapters handle both non-streaming (`GetResponseAsync`) and streaming (`GetStreamingResponseAsync`) responses
- Tool/function calling is supported across all three API shapes with proper serialization of `FunctionCallContent`/`FunctionResultContent`
- Streaming adapters accumulate tool call fragments and emit them as complete `FunctionCallContent` updates
- JSON serialization uses `System.Text.Json` with `JsonNode`/`JsonObject` for request building and typed inner classes for response parsing

### Source Generators

`ManInBlack.AI.SourceGenerator` (targets .NET Standard 2.0) contains three incremental generators:

| Generator | Output | Purpose |
|-----------|--------|---------|
| `ToolCallerGenerator` | `ToolCaller.g.cs` | Generates a `ToolCaller` class that dispatches `[AiTool]` method calls at runtime via `IServiceProvider` |
| `ToolDeclarationGenerator` | `{Type}.ToolDeclarations.g.cs` | Generates `AIFunctionDeclaration` static members and `AllToolDeclarations` array for each partial class with `[AiTool]` methods |
| `ServiceRegistrationGenerator` | `ServiceRegistrationExtensions.g.cs` | Generates DI registration extensions for `[ServiceRegister]`-attributed classes |

All emitters use **Fengb3.EasyCodeBuilder** for code generation. When generating new emitters, follow the same pattern: use `Code.Create().Using(...).Namespace(ns => ...)` with `Code.Build(option, new CodeBuilder())`.

### Tool declaration pipeline

1. `[AiTool]` marks methods as AI-callable tools
2. `ToolDeclarationGenerator` extracts method signatures + XML doc comments (`<summary>`, `<param>`, `<returns>`)
3. For each containing partial class, generates `ToolFunctionDeclaration` fields with JSON Schema for parameters/returns
4. `ToolFunctionDeclaration` (in `ManInBlack.AI/Tools/`) is a concrete `AIFunctionDeclaration` subclass that parses JSON Schema strings at construction

### Diagnostic rules

| ID | Severity | Trigger |
|----|----------|---------|
| MIB001 | Error | `[ServiceRegister.X.As<T>]` where type doesn't implement T |
| MIB010 | Error | Class with `[AiTool]` methods is not `partial` |
| MIB011 | Warning | `[AiTool]` method missing `<summary>` XML doc |
| MIB012 | Warning | `[AiTool]` method parameter missing `<param>` XML doc |
| MIB013 | Warning | Non-void `[AiTool]` method missing `<returns>` XML doc |

### Files with commented-out code

`ServiceCollectionExtensions.cs`, `ChatClientFactory.cs`, and `AdditionalProviders.cs` are entirely commented out. They represent an older iteration of the DI/factory pattern. The active code uses the simpler `ModelProvider` + `ChatClientProviderExtensions` approach instead.

## Dependencies

### ManInBlack.AI
- `Microsoft.Extensions.AI` 10.4.1 — Core `IChatClient` / `ChatMessage` / `AITool` / `AIFunctionDeclaration` abstractions
- `Microsoft.Extensions.Http` 10.0.0 — `IHttpClientFactory` integration
- `ModelContextProtocol` 1.2.0 — MCP protocol support

### ManInBlack.AI.SourceGenerator
- `Fengb3.EasyCodeBuilder` 0.1.4 — Fluent code generation API
- `Microsoft.CodeAnalysis.CSharp` 4.11.0 — Roslyn API for source generation

## Code Style

- Comments and XML docs are in Chinese
- C# 10 with file-scoped namespaces, implicit usings, nullable enabled
- IDE: JetBrains Rider
- Source generator emitters must use EasyCodeBuilder, not raw StringBuilder
