# AGENTS.md — ManInBlack

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

No linter, formatter, or CI pipeline configured. No `global.json`, `Directory.Build.props`, or `.editorconfig`.

## Critical traps

These are non-obvious constraints that agents frequently get wrong:

- **DI uses C# 13 `extension` syntax** — NOT static extension methods. See `src/ManInBlack.AI/DependencyInjection.cs`.
- **Source generators use `Fengb3.EasyCodeBuilder` only** — never raw `StringBuilder`. → [sourcegenerator-guide.md](docs/sourcegenerator-guide.md)
- **`[AiTool]` classes must be `partial`** — compiler error MIB010 if not. XML docs on `[AiTool]` methods/params become LLM-visible descriptions. → [sourcegenerator-guide.md](docs/sourcegenerator-guide.md)
- **`AgentLoopMiddleware` must always be the innermost** (last registered) middleware. → [middleware-guide.md](docs/middleware-guide.md)

## Conventions

- All comments and docs in Chinese. Maintain this convention.
- Commit messages use [gitmoji](https://gitmoji.dev/) prefix. **NEVER** add `Co-authored-by` trailers.
- Tests use hand-written fakes, no mocking frameworks (except FeishuAdaptor.Tests uses NSubstitute).

## Docs index

Read the corresponding doc before modifying a module:

| Topic | Doc |
|---|---|
| Architecture, project layers, pipeline | [docs/architecture.md](docs/architecture.md) |
| Agent factory, definition, lifecycle | [docs/agent-factory-guide.md](docs/agent-factory-guide.md) |
| Middleware development & pipeline order | [docs/middleware-guide.md](docs/middleware-guide.md) |
| Source generators & diagnostic rules | [docs/sourcegenerator-guide.md](docs/sourcegenerator-guide.md) |
| Tool development & `[AiTool]` pattern | [docs/tools-guide.md](docs/tools-guide.md) |
| Configuration system (`IOptions`, `settings.json`) | [docs/configuration-guide.md](docs/configuration-guide.md) |
| Hooks (external shell scripts) | [docs/hooks-guide.md](docs/hooks-guide.md) |
| Testing middleware | [docs/testing-guide.md](docs/testing-guide.md) |
| Provider configuration & 3 adapter protocols | [docs/provider-guide.md](docs/provider-guide.md) |
| Feishu adaptor | [docs/feishu-guide.md](docs/feishu-guide.md) |
| Quick start guide | [docs/quick-start.md](docs/quick-start.md) |
