# `[AiTool]` 复杂对象/数组参数支持 设计

> 日期：2026-07-29
> 状态：待批准
> 关联：本修复是后续「AskUser 提问工具」特性的前置依赖（AskUser 需 `List<ChoiceOption>` 入参）。

## 背景

源生成器为 `[AiTool]` 方法生成参数提取代码与 JSON Schema，但两端都**只支持标量类型**（`string`/`bool`/数值/`DateTime`）。任何对象、数组、集合参数都会静默坏掉：LLM 拿到错误 schema，运行时拿到 `null`。全仓库现有 `[AiTool]`（RunBash、FileTools、DelegateToAgent 等）一律只用标量参数，从未踩到。

## 问题证据（两端）

**① schema 端** —— `ToolCallerEmitter.MapToJsonSchemaType`（`ToolCallerEmitter.cs:363`）default 分支把一切非标量兜底成 `"object"`：

```csharp
_ => "object"   // List<ChoiceOption>、ChoiceOption[]、ChoiceOption、enum 全落这里
```

于是 `List<ChoiceOption>` 参数给 LLM 的 schema 是 `{"type":"object"}`——既非 `array`，也无 `items`/属性。模型不知该传数组。

**② 取值端** —— `ToolCallerEmitter.ConvertExpr`（`ToolCallerEmitter.cs:251`）对引用类型（`IsValueType=false`）生成裸 cast：

```csharp
return $"{varName} as {targetType}";   // 生成：options_raw as List<ChoiceOption>
```

而运行时 `Arguments = fc.Arguments`（`AgentLoopMiddleware.cs:101`），`Microsoft.Extensions.AI` 把模型 JSON 参数解析成 `JsonElement`。`JsonElement as List<ChoiceOption>` → **`null`**，后续使用即 NRE。

**schema 确实会发给 LLM**：`OpenAICompatibleChatClient.cs:315` `funcObj["parameters"] = JsonNode.Parse(aiFuncDecl.JsonSchema.GetRawText())`，声明里的 schema 字符串**原样**作为工具 `parameters` 发出。所以修好 schema 端，LLM 立刻看到正确结构。

## 目标与非目标

**做**：让 `[AiTool]` 支持复杂对象 / 数组 / 集合参数，两端都修：
- schema 端：对象 → `object`+属性；数组/集合 → `array`+`items`；可嵌套。
- 取值端：`JsonElement` 正确反序列化为声明类型。

**顺带修**：enum 参数（现被误判为 `"object"`）→ `"string"` + `enum` 成员名。

**不做**：AskUser 工具本身（下一轮）；字典 / 元组 / open generic 支持（这类类型直接编译报错）。

## 设计

### 1. schema 构造上移到 generator 端（核心）

现状 `BuildTypeSchemaJson` 在 emitter 里，只有类型名字符串、无 Roslyn 符号，无法递归看嵌套属性。改在 **generator 端**（`ToolCallerGenerator.GetToolMethodModel` 有 `IParameterSymbol p` → `p.Type` 为 `ITypeSymbol`）写递归 `BuildJsonSchema(ITypeSymbol, int depth)`，产出 JSON Schema 字符串，存入 `ToolParameterModel` 新增字段 `JsonSchema`；emitter 的 `BuildParametersJsonSchema` 直接插入该字符串，不再自拼。

产出示例：

```jsonc
// List<ChoiceOption>
{"type":"array","items":{"type":"object","properties":{"label":{"type":"string"},"description":{"type":"string"}},"required":["label"]}}

// ChoiceOption
{"type":"object","properties":{"label":{"type":"string"},"description":{"type":"string"}},"required":["label"]}

// Color（enum）
{"type":"string","enum":["Red","Green","Blue"]}
```

> 返回值 schema（`BuildReturnJsonSchema`）不迁移、仍走 emitter 的 `BuildTypeSchemaJson`/`MapToJsonSchemaType`；仅顺带给后者补 enum 分支（见 §7），让返回 enum 的工具也受益。现有工具返回值均为 `string`/`void`，不受影响。本轮改动聚焦参数。

### 2. 取值端：引用类型统一反序列化（核心）

`ConvertExpr` 引用类型分支（非 `string`、非值类型）改为：

```csharp
// 生成的代码
options is List<ChoiceOption> o ? o
  : options is System.Text.Json.JsonElement je
      ? je.Deserialize<List<ChoiceOption>>(ToolArgumentJsonOptions.Default)
      : null
```

`System.Text.Json` 可直接反序列化到声明的任意类型（`T[]`/`List<T>`/对象/enum），故引用类型分支**统一这一套**。emitter 新增 `using System.Text.Json;`。

### 3. 共享 JSON 选项

新增 `ToolArgumentJsonOptions`（放 `ManInBlack.AI.Abstraction/Tools/`，已在生成代码 usings 内）：

```csharp
public static class ToolArgumentJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,                          // LLM 可能回 camelCase 或 PascalCase，都兜住
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
```

### 4. 支持的类型集

| 类别 | 类型 | schema |
|------|------|--------|
| 标量 | `string`/`bool`/数值/`char`/`DateTime`/`DateTimeOffset` | 沿用现逻辑 |
| enum | 任意 enum | `{"type":"string","enum":[成员名]}`，取值端 `Deserialize<枚举>`（字符串/数字皆可） |
| 数组 | `T[]` | `{"type":"array","items":<T>}` |
| 集合 | `List<T>`/`IList<T>`/`ICollection<T>`/`IReadOnlyList<T>`/`IReadOnlyCollection<T>`/`IEnumerable<T>`/`HashSet<T>`/`ISet<T>`/`IReadOnlySet<T>` | `{"type":"array","items":<T>}` |
| 对象 | record / class | `{"type":"object","properties":{...},"required":[...]}`，成员 = **公共可读实例属性**（无参 getter、非静态）；`required` = 非可空且无默认值的属性 |
| 可空 | `T?`（引用）/`Nullable<T>`（值） | unwrap 内层 T；引用可空 schema 用 `["T","null"]` |

**集合识别规则**：先判 `IArrayTypeSymbol`；再判是否「白名单集合」（精确匹配上表 generic 定义）；否则视为对象走属性。避免把「恰好实现 `IEnumerable` 的 POCO」误判为数组。`string` 在标量分支先行命中，不会被当集合。

### 5. 嵌套与深度上限

对象属性可递归（如 `List<Foo>`，`Foo` 含 `Bar[]`）。**深度上限 4**：超出后该层退化为无 `properties` 的 `{"type":"object"}`（**不报错**，仅降级），防自引用/超深嵌套死循环。`string` 等标量不消耗深度。

### 6. 不支持的类型 → 编译报错（MIB014）

不在上表内的类型（`Dictionary<,>`、tuple、`object`、open generic、`Type`、`JsonElement` 作参数 等）→ 新诊断 **MIB014（Error）**：

> `[AiTool] 方法 '{0}' 的参数 '{1}' 类型 '{2}' 不受源生成器支持，请改用受支持的类型（标量/enum/对象/数组/集合）。`

报错走现有 `ReportDiagnostics` 模式（与 MIB010 同）。build 失败，逼着改参数类型，杜绝静默坏掉。

### 7. enum 修复（顺带）

`MapToJsonSchemaType` 的 enum 分支：`ITypeSymbol.TypeKind == Enum` → `{"type":"string","enum":[成员名]}`。取值端 `Deserialize<枚举>` 兼容字符串与数字。

## 涉及文件

| 文件 | 变更 |
|------|------|
| `src/ManInBlack.AI.SourceGenerator/ToolMethodModel.cs` | `ToolParameterModel` 新增 `JsonSchema`、`IsUnsupportedType`、`UnsupportedReason` 字段 |
| `src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs` | 新增递归 `BuildJsonSchema(ITypeSymbol, depth)` 与集合/enum/对象识别；填充 `JsonSchema` 与不支持标记；新增 MIB014 描述符并在 `ReportDiagnostics` 上报 |
| `src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs` | `BuildParametersJsonSchema` 改用 `param.JsonSchema`；`ConvertExpr` 引用类型分支改 `JsonElement.Deserialize<T>`；新增 `using System.Text.Json;`；`MapToJsonSchemaType` enum 分支（仍服务于返回值 schema） |
| `src/ManInBlack.AI.Abstraction/Tools/ToolArgumentJsonOptions.cs` | 新增：共享 `JsonSerializerOptions` |
| `docs/sourcegenerator-guide.md` | 诊断规则表加 MIB014；说明复杂参数支持与受支持类型集 |
| `docs/tools-guide.md` | 补「复杂对象/数组参数」用法与示例 |
| `test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs` | 新增（见测试计划） |

## 测试计划

新增 `test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs`（该项目已引用源生成器，定义 `[AiTool]` 即生成 handler）：

1. **schema 正确性**：定义带 `List<ChoiceOption>` / `ChoiceOption` / `ChoiceOption[]` / enum 参数的测试工具；取其 `ToolDeclaration.JsonSchema`，断言含 `type:array`/`items`、嵌套 `properties`、`required`、enum 的 `enum` 值。
2. **取值反序列化（核心）**：构造 `ToolExecuteContext`，`Arguments` 塞 `JsonElement.Parse("[{\"label\":\"A\",\"description\":\"x\"}]")`（模拟 `FunctionCallContent.Arguments`），经真实 `ToolExecutor` 派发到生成 handler，断言工具收到正确的 `List<ChoiceOption>`（label/description 对号）。
3. **大小写兼容**：分别用 `{"Label":"A"}`（PascalCase）与 `{"label":"A"}`（camelCase）两用例，验证 `PropertyNameCaseInsensitive` 生效。
4. **enum 取值**：enum 参数分别传 `"Red"` 与 `0`，均正确还原。
5. **MIB014 诊断**：带 `Dictionary<string,string>` 参数的工具 → 断言编译产生 MIB014 Error（参考 `SlashCommandGeneratorTests` 的生成器诊断测试模式）。
6. **深度上限**：构造自引用或 5 层嵌套类型 → schema 在第 4 层降级为无 `properties` 的 object，不报错、不死循环。

约定：单元测试用手写 fake，不用 mock 框架（遵循 AGENTS.md，`FeishuAdaptor.Tests` 除外）。

## 风险与回滚

- **风险**：递归 schema 构造对罕见类型（泛型嵌套、`Nullable<T>` unwrap 边界）误判。缓解：白名单严格 + 深度上限 + MIB014 报错兜底；测试覆盖典型与边界。
- **回归**：现有标量工具的 schema/取值行为必须不变（生成代码对标量分支零改动）。回归基线：`dotnet test test/ManInBlack.AI.Tests` 全绿 + 现有 demo（AgentConsole/FeishuAdaptor）`dotnet build` 通过。
- **回滚**：改动集中在源生成器 + 一个新 Abstraction 类，单 commit 可回退。

## 下一轮衔接

本修复落地后，`AskUser` 工具即可声明 `AskUser(string question, List<ChoiceOption> options, bool multiSelect)`（强类型，无需 `optionsJson` 字符串绕行），schema 自然产出 `array of object`。
