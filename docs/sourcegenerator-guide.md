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
| MIB014 | Error    | `[AiTool]` 参数类型不受支持（字典/tuple/open generic/`object` 等） |

## 复杂参数类型支持

`[AiTool]` 方法参数支持标量、enum、对象（POCO/record，取公共可读实例属性）、数组与集合。
schema 由源生成器从 Roslyn `ITypeSymbol` 递归生成；运行时由生成的 handler 用
`JsonElement.Deserialize<T>(ToolArgumentJsonOptions.Default)` 反序列化（大小写不敏感，enum 用 `JsonStringEnumConverter`）。

受支持集合：`T[]`、`List<T>`、`IList<T>`、`ICollection<T>`、`IReadOnlyList<T>`、
`IReadOnlyCollection<T>`、`IEnumerable<T>`、`HashSet<T>`、`ISet<T>`、`IReadOnlySet<T>`、
`Queue<T>`、`Stack<T>`、`LinkedList<T>`。

对象 schema 的成员取**公共可读实例属性**，schema 属性名转 camelCase；`required` = 非可空属性。
嵌套 schema 深度上限 4，超出降级为不透明 `object`（防自引用死循环）。
`Dictionary<,>`、元组、开放泛型、`object` 等不受支持 → MIB014 报错。
