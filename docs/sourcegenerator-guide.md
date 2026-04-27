# Source Generator & 诊断规则

> 本文档是 CLAUDE.md 的子文档，Agent 在修改 Source Generator、`[AiTool]`、`[ServiceRegister]` 相关代码前应先阅读此文档。

## Source generators

| Generator                      | Purpose                                                                            |
|--------------------------------|------------------------------------------------------------------------------------|
| `ToolCallerGenerator`          | Dispatches `[AiTool]` method calls via `IServiceProvider`                          |
| `ToolDeclarationGenerator`     | Generates `AIFunctionDeclaration` + JSON Schema from `[AiTool]` methods + XML docs |
| `ServiceRegistrationGenerator` | DI registration for `[ServiceRegister]`-attributed classes                         |

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
