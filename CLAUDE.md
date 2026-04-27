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

.NET 10 library providing unified abstractions for multiple AI chat providers through `Microsoft.Extensions.AI`'s
`IChatClient` interface.

Layout: `src/` · `demo/` · `test/`

## Code Style

- Comments and XML docs in Chinese
- C# file-scoped namespaces, implicit usings, nullable enabled
- C# extension syntax for DI (`extension(IServiceCollection services)`)
- Source generator emitters must use EasyCodeBuilder, not raw StringBuilder
- IDE: JetBrains Rider & VS Code (with C# extensions)

## Documentation

**Agent 在修改相关模块的代码前，应先阅读对应的文档，了解该模块的架构和约束。**

### Agent 参考文档

- 架构概览（ChatClient 层、管道、DI、工具调用流程）: [architecture](docs/architecture.md)
- 中间件开发指北（管道模型、AgentContext、编写模式、注册顺序）: [middleware-guide](docs/middleware-guide.md)
- 配置指南（settings.json、DI 注册方式、校验）: [configuration-guide](docs/configuration-guide.md)
- Source Generator & 诊断规则: [sourcegenerator-guide](docs/sourcegenerator-guide.md)
- 工具开发指北（AiTool、ToolCallFilter、自定义工具）: [tools-guide](docs/tools-guide.md)
- 飞书适配器: [feishu-guide](docs/feishu-guide.md)

### 用户文档

- Provider 配置指南: [provider-guide](docs/provider-guide.md)
- 快速开始: [quick-start](docs/quick-start.md)
- 中间件测试指北: [testing-guide](docs/testing-guide.md)
