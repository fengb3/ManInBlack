# Source Generator & 诊断规则

> 本文档是 CLAUDE.md 的子文档，Agent 在修改 Source Generator、`[AiTool]`、`[ServiceRegister]` 相关代码前应先阅读此文档。

## Source generators

| Generator                      | Purpose                                                                            |
|--------------------------------|------------------------------------------------------------------------------------|
| `ToolCallerGenerator`          | 为每个 `[AiTool]` 方法生成独立 `IToolHandler` + `IToolDeclaration`，注册到 DI      |
| `ServiceRegistrationGenerator` | DI registration for `[ServiceRegister]`-attributed classes                         |

`ToolCallerGenerator` 同时负责：
- 生成 per-tool handler 类（实现 `IToolHandler`）
- 生成工具声明（`ToolFunctionDeclaration`）并注册为 `IToolDeclaration`
- 生成 `ToolExecutor`（字典查找分发）和 `AddToolHandlers()` DI 扩展方法

All emitters use **Fengb3.EasyCodeBuilder** (`Code.Create().Using(...).Namespace(ns => ...)` /
`Code.Build(option, new CodeBuilder())`).

## 诊断规则

| ID     | Severity | Trigger                                                    |
|--------|----------|------------------------------------------------------------|
| MIB001 | Error    | `[ServiceRegister.X.As<T>]` where type doesn't implement T |
| MIB010 | Error    | Class with `[AiTool]` methods is not `partial`             |
| MIB011 | Warning  | `[AiTool]` method missing `<summary>`                      |
| MIB012 | Warning  | `[AiTool]` parameter missing `<param>`                     |
| MIB013 | Warning  | Non-void `[AiTool]` missing `<returns>`                    |
