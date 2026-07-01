# AGENTS.md — ManInBlack

.NET 10 AI agent 框架。通过 `Microsoft.Extensions.AI`（`IChatClient`）统一抽象 3 种聊天协议（OpenAI/Anthropic/Gemini）。洋葱模型中间件管道，源生成器工具派发，Linux 下通过 bubblewrap 沙盒执行。

## 构建与测试

```bash
dotnet build ManInBlack.slnx                                    # 构建全部
dotnet build src/ManInBlack.AI                                  # 仅构建主库
dotnet build src/ManInBlack.AI.SourceGenerator                  # 源生成器（netstandard2.0）
dotnet test test/ManInBlack.AI.Tests                            # 单元测试（xunit）
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~OpenAI"  # 过滤测试
dotnet test test/FeishuAdaptor.Tests                            # 飞书适配器测试（NSubstitute）
dotnet run --project demo/AgentConsole                          # 控制台 demo
dotnet run --project demo/FeishuAdaptor                         # 飞书 bot demo
dotnet run --project demo/Dashboard                            # Dashboard API（:5080）
dotnet run --project demo/AppHost                              # Aspire:同时启动飞书 + Dashboard + 前端
cd demo/Dashboard/client && npm run dev                        # Dashboard 前端（:5173）
dotnet publish demo/Dashboard -c Release                       # 发布（含前端构建）
dotnet test test/Dashboard.Tests                               # Dashboard 测试
```

未配置 linter、formatter 或 CI 管道。无 `global.json`、`Directory.Build.props` 或 `.editorconfig`。

## 关键陷阱

这些是 Agent 经常踩坑的非显而易见的约束：

- **DI 使用 C# 13 `extension` 语法** — 不是静态扩展方法。参见 `src/ManInBlack.AI/DependencyInjection.cs`。
- **源生成器只能用 `Fengb3.EasyCodeBuilder`** — 禁止使用原始 `StringBuilder`。→ [源生成器指南](docs/sourcegenerator-guide.md)
- **`[AiTool]` 类必须声明为 `partial`** — 否则编译器报错 MIB010。`[AiTool]` 方法和参数上的 XML 文档注释会成为 LLM 可见的描述。→ [源生成器指南](docs/sourcegenerator-guide.md)
- **`AgentLoopMiddleware` 必须始终是最内层**（最后注册的）中间件。→ [中间件开发指北](docs/middleware-guide.md)

## 约定

- 所有注释和文档使用中文。
- 提交信息使用 [gitmoji](https://gitmoji.dev/) 前缀。**禁止**添加 `Co-authored-by` 尾部。
- 测试使用手写 fake，不使用 mock 框架（FeishuAdaptor.Tests 除外，使用 NSubstitute）。
- 修改模块后必须同步更新 `docs/` 下对应文档的内容（配置示例、代码片段、字段说明等）。

## 文档索引

修改模块前先阅读对应文档：

- [架构概览](docs/architecture.md)
- [Agent 工厂指南](docs/agent-factory-guide.md)
- [中间件开发指北](docs/middleware-guide.md)
- [源生成器指南](docs/sourcegenerator-guide.md)
- [工具开发指北](docs/tools-guide.md)
- [配置指南](docs/configuration-guide.md)
- [Hook 开发指北](docs/hooks-guide.md)
- [事件总线指南](docs/eventbus-guide.md)
- [测试指北](docs/testing-guide.md)
- [Provider 配置指南](docs/provider-guide.md)
- [飞书适配器指南](docs/feishu-guide.md)
- [快速开始](docs/quick-start.md)
- [存储指南](docs/storage-guide.md)
- [Dashboard 指南](docs/dashboard-guide.md)
- [Aspire 编排指南](docs/aspire-guide.md)
