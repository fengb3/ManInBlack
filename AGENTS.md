# AGENTS.md — ManInBlack

> Agent-facing quick reference. For full architectural detail, see `docs/architecture.md` and the other guides linked in `CLAUDE.md`.

## What this repo is

.NET 10 AI agent framework. Unified abstractions for 15 chat providers through `Microsoft.Extensions.AI` (`IChatClient`). Onion-model middleware pipeline, source-generated tool dispatch, sandbox execution on Linux via bubblewrap.

## Build & test

```bash
dotnet build ManInBlack.slnx                                    # Build everything
dotnet build src/ManInBlack.AI                                  # Main library only
dotnet build src/ManInBlack.AI.SourceGenerator                  # Source generator (netstandard2.0)
dotnet test test/ManInBlack.AI.Tests                            # Unit tests (xunit)
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~OpenAI"  # Filtered
dotnet test test/FeishuAdaptor.Tests                            # Feishu adaptor tests (NSubstitute)
dotnet run --project demo/AgentConsole                          # Console demo
dotnet run --project demo/FeishuAdaptor                         # 飞书 bot demo
```

No linter, formatter, or CI pipeline configured.

## Project structure & dependency graph

```
src/ManInBlack.AI.Abstraction   ← interfaces, abstract classes, POCO, attributes
  │  depends on: Microsoft.Extensions.AI 10.5.0 only
  │
  ├── src/ManInBlack.AI         ← all implementations: ChatClient adapters, middlewares, tools, DI
  │     depends on: Abstraction, Bwarp, SourceGenerator (as analyzer)
  │     key deps: M.E.Configuration.Json, M.E.Http, M.E.Logging, ModelContextProtocol 1.2.0
  │
  ├── src/ManInBlack.AI.SourceGenerator  ← netstandard2.0 incremental source generators
  │     depends on: Microsoft.CodeAnalysis.CSharp 4.11.0, Fengb3.EasyCodeBuilder 0.1.6
  │
  └── bwarp/Bwarp               ← bubblewrap sandbox wrapper (Linux only)
        depends on: nothing external

demo/
  AgentConsole   ← console demo (Exe, references AI + SG)
  Playground     ← M.E.AI type explorer (Exe, references AI + SG)
  FeishuAdaptor  ← 飞书 bot (Web, references AI + SG, has Docker support)

test/
  ManInBlack.AI.Tests          ← unit tests (FakeStorage, FakeToolExecutor, MockHttpMessageHandler)
  FeishuAdaptor.Tests          ← Feishu-specific tests (NSubstitute)
  ManInBlack.AI.IntegrationTests ← currently empty (only bin/obj artifacts)
```

## Critical conventions

### DI registration uses C# 13 `extension` syntax

```csharp
// In DependencyInjection.cs
extension(IServiceCollection services)
{
    public IServiceCollection AddManInBlack(Action<ManInBlackOptions> configure) { ... }
    public IServiceCollection AddManInBlackFromSettings(...) { ... }
    public IServiceCollection AddManInBlackFromConfiguration(IConfiguration, ...) { ... }
}
```

This is NOT extension methods — it's the new C# 13 extension member syntax. Don't rewrite as static extension methods.

### Source generators: EasyCodeBuilder only

All emitters in `src/ManInBlack.AI.SourceGenerator/` use `Fengb3.EasyCodeBuilder` (`Code.Create().Using(...).Namespace(...)`) — never raw `StringBuilder`. When adding/modifying generators, follow this pattern exactly. See `ToolCallerEmitter.cs` for reference.

### Four source generators

| Generator | Generates | Attribute |
|---|---|---|
| `ToolCallerGenerator` | `ToolExecutor : IToolExecutor` dispatcher | `[AiTool]` on methods |
| `ToolDeclarationGenerator` | JSON Schema tool declarations | `[AiTool]` + XML docs |
| `ServiceRegistrationGenerator` | `AddAutoRegisteredServices()` DI extension | `[ServiceRegister.Scoped/Singleton/Transient]` on classes |
| `ToolMiddlewareGenerator` | Per-tool middleware + DI registration | `[AiTool]` methods |

### Diagnostic rules (compile-time)

| ID | Severity | Trigger |
|---|---|---|
| MIB001 | Error | `[ServiceRegister.X.As<T>]` type doesn't implement T |
| MIB010 | Error | Class with `[AiTool]` methods is not `partial` |
| MIB011 | Warning | `[AiTool]` method missing `<summary>` |
| MIB012 | Warning | `[AiTool]` parameter missing `<param>` |
| MIB013 | Warning | Non-void `[AiTool]` missing `<returns>` |

**Any class with `[AiTool]` methods must be `partial`.** XML doc comments on `[AiTool]` methods and parameters become the tool's description sent to the LLM.

### Middleware pipeline (onion model)

```csharp
// Registration order = outer-to-inner execution order
builder.Use<A>().Use<B>().Use<C>().Build(sp);
// Runtime: A.pre → B.pre → C.pre → IChatClient → C.post → B.post → A.post
```

Default pipeline (`UseDefault`): `ReadPersistence → SavePersistence → Skill → AgentProfile → ContextCompress → CommandLineTools → FileTools → UseSimple`

`UseSimple`: `Logging → MessageEnrich → Hook → SystemPromptInjection → UserInput → Retry → AgentLoop`

**AgentLoopMiddleware must always be the innermost (last registered).**

### `[AiTool]` pattern for new tools

1. Create a `partial class` with `[ServiceRegister.Scoped]` on the class
2. Mark methods with `[AiTool]`
3. Add XML doc `<summary>`, `<param>`, `<returns>` (these become LLM-visible descriptions)
4. Optionally add `[AiTool.HasFilterAttribute<T>]` for tool call filters
5. Source generators handle: dispatch, declaration, middleware, and DI registration

### Tool call filters

Chain of filters applied to tool invocations. Built-in: `LoggingFilter`, `BroadCastingFilter`, `LargeResultFilter`. Attach via `[AiTool.HasFilterAttribute<LogFilter>]` on the tool method.

### Configuration

- Config loaded from `~/.man-in-black/settings.json`
- Uses standard `IConfiguration` + `IOptions<T>` with `reloadOnChange: true`
- `IValidateOptions<ManInBlackSettings>` validates `ApiKey` presence
- Entry points: `AddManInBlackFromSettings()` or `AddManInBlackFromConfiguration(IConfiguration)`

### Hooks are external scripts, not C#

Hooks run **shell scripts** configured in JSON (global: settings.json, user: `.agents/mib-hooks.json`). `HookExecutor` serializes context to a temp JSON file, passes it as arg to the script, parses `HookResult` from stdout. Not C# callbacks. Hook points: `BeforeLlmCall`, `AfterLlmCall`, `BeforeToolExecute`, `AfterToolExecute`, `AllToolsCompleted`, `AgentCompleted`.

### Testing: hand-written fakes, no mocking frameworks

`ManInBlack.AI.Tests` uses xUnit with hand-written in-memory fakes in `Helpers/` — no Moq/NSubstitute. Pattern: create middleware with fakes, call `HandleAsync` with a crafted `next` delegate, `ToListAsync()` results, assert on `ctx.Messages` and returned updates. `FeishuAdaptor.Tests` uses NSubstitute separately.

### Build config notes

- No `global.json` — SDK version is not pinned
- No `Directory.Build.props` or `Directory.Packages.props` — no centralized build properties or Central Package Management
- No `.editorconfig` — no enforced formatting rules
- Test projects use `Version="*"` for xunit/Test SDK packages (unpinned, resolved at restore)
- `ManInBlack.AI.SourceGenerator` targets `netstandard2.0` (required for Roslyn analyzers — no net10.0 APIs available)

### Shell execution sandbox

- **Linux**: `BwarpShellExecutor` wraps `bubblewrap` for sandboxed command execution
- **Windows/macOS**: `ProcessShellExecutor` uses `Process.Start` directly
- Selected automatically via `OperatingSystem.IsLinux()` in DI registration

### Comments and docs in Chinese

All XML doc comments, code comments, and documentation are in Chinese. Maintain this convention.

## Key file locations

| Concern | Files |
|---|---|
| DI entry point | `src/ManInBlack.AI/DependencyInjection.cs` |
| Pipeline defaults | `src/ManInBlack.AI/AgentPipelines.cs` |
| All providers | `src/ManInBlack.AI/Providers.cs` |
| ChatClient adapters | `src/ManInBlack.AI/ChatClient/` (OpenAI/Anthropic/Gemini) |
| Middleware implementations | `src/ManInBlack.AI/Middlewares/` (14 files) |
| Tool implementations | `src/ManInBlack.AI/Tools/` (CommandLine, File, Skill) |
| Abstraction layer | `src/ManInBlack.AI.Abstraction/` (interfaces, attributes, middleware base) |
| Source generators | `src/ManInBlack.AI.SourceGenerator/` (4 generators + emitters) |
| Configuration | `src/ManInBlack.AI/Configuration/` |
| Test helpers | `test/ManInBlack.AI.Tests/Helpers/` (FakeStorage, FakeToolExecutor, MockHttpMessageHandler, SseResponseBuilder) |
| DefaultSkills | `src/ManInBlack.AI/DefaultSkills/` (skill-creator, skill-installer) |

## Commit rules

- Use [gitmoji](https://gitmoji.dev/) prefix in commit messages (e.g. `✨ add feature`, `🐛 fix null ref`)
- **NEVER** add `Co-authored-by` or `Co-Authored-By` trailers.

## Before modifying a module

Read the corresponding doc first — they capture architecture constraints that aren't obvious from code:

- Middleware changes → `docs/middleware-guide.md`
- Source generator changes → `docs/sourcegenerator-guide.md`
- Config system changes → `docs/configuration-guide.md`
- Tool development → `docs/tools-guide.md`
- Hooks → `docs/hooks-guide.md`
- Feishu adaptor → `docs/feishu-guide.md`
- Testing middleware → `docs/testing-guide.md`

## Three ChatClient adapter protocols

15 providers map to 3 wire protocols via `CompatibleWith`:

| Protocol | Providers |
|---|---|
| `OpenAI` (SSE `data: ... [DONE]`) | OpenAI, Kimi, DeepSeek, Qwen, Zhipu, Yi, Baichuan, StepFun, Spark, Doubao, MiniMax |
| `Anthropic` (SSE `content_block_*`) | Anthropic |
| `Gemini` (SSE + API key in query) | Gemini |

## EmitCompilerGeneratedFiles

Demo projects (`AgentConsole`, `Playground`) set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` to surface generated code. They also exclude generated files from compilation: `<Compile Remove="$(BaseIntermediateOutputPath)/**/*.cs" />`.
