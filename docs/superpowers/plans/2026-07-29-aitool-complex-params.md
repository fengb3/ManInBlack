# `[AiTool]` 复杂对象/数组参数支持 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `[AiTool]` 方法支持复杂对象 / 数组 / 集合 / enum 参数：schema 端给 LLM 正确的 `object`/`array`/`enum` 结构，运行时把 `JsonElement` 正确反序列化为声明类型。

**Architecture:** 两端同改。① schema 构造从 emitter 上移到源生成器端（`ToolCallerGenerator` 持有 `ITypeSymbol`，可递归生成对象/数组 schema），结果存入 `ToolParameterModel.JsonSchema`，emitter 直接插入。② `ConvertExpr` 引用类型分支改用 `JsonElement.Deserialize<T>(ToolArgumentJsonOptions.Default)`。不受支持的类型（字典/tuple 等）报新诊断 **MIB014（Error）**。

**Tech Stack:** .NET 10、Roslyn IIncrementalGenerator、`Fengb3.EasyCodeBuilder`、`System.Text.Json`、xunit（手写 fake，不用 mock 框架）。

> 约定：提交信息用 [gitmoji](https://gitmoji.dev/) 前缀，**禁止** `Co-authored-by` 尾部（AGENTS.md）。所有注释/文档用中文。源生成器禁止原始 `StringBuilder`，必须用 `Fengb3.EasyCodeBuilder`。

---

## 文件结构

| 文件 | 责任 |
|------|------|
| `src/ManInBlack.AI.Abstraction/Tools/ToolArgumentJsonOptions.cs` | 新增。生成代码反序列化用的共享 `JsonSerializerOptions`（`PropertyNameCaseInsensitive=true`）。 |
| `src/ManInBlack.AI.SourceGenerator/ToolMethodModel.cs` | 改。`ToolParameterModel` 新增 `JsonSchema`、`IsUnsupportedType`、`UnsupportedReason`。 |
| `src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs` | 改。新增递归 `BuildJsonSchema(ITypeSymbol, ...)`；在 `GetToolMethodModel` 填充 `JsonSchema`/不支持标记；新增 MIB014 描述符并在 `ReportDiagnostics` 上报。 |
| `src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs` | 改。`BuildParametersJsonSchema` 改用 `param.JsonSchema`；`ConvertExpr` 引用类型分支改 `JsonElement.Deserialize<T>`；新增 `using System.Text.Json`；`MapToJsonSchemaType` 补 enum 分支。 |
| `test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj` | 改。加源生成器 analyzer 引用，使测试项目里的 `[AiTool]` 生成 handler。 |
| `test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs` | 新增。带复杂参数的测试 `[AiTool]` 工具 + `ChoiceOption`/`Color`/`Node` 类型。 |
| `test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs` | 新增。schema 正确性 + 运行时反序列化 + 大小写 + enum + 深度上限。 |
| `test/ManInBlack.AI.Tests/Tools/GeneratorDriverHelper.cs` | 新增。Roslyn `CSharpGeneratorDriver` 测试助手（用于 MIB014 诊断测试）。 |
| `test/ManInBlack.AI.Tests/Tools/AiToolUnsupportedParamTests.cs` | 新增。MIB014 诊断测试。 |
| `docs/sourcegenerator-guide.md` | 改。诊断规则表加 MIB014；补复杂参数支持与受支持类型集。 |
| `docs/tools-guide.md` | 改。补「复杂对象/数组参数」用法与示例。 |

---

## Task 1: 共享 JSON 反序列化选项

**Files:**
- Create: `src/ManInBlack.AI.Abstraction/Tools/ToolArgumentJsonOptions.cs`
- Test: `test/ManInBlack.AI.Tests/Tools/ToolArgumentJsonOptionsTests.cs`

- [ ] **Step 1: 写失败测试**

Create `test/ManInBlack.AI.Tests/Tools/ToolArgumentJsonOptionsTests.cs`:

```csharp
using ManInBlack.AI.Abstraction.Tools;
using Xunit;

namespace ManInBlack.AI.Tests.Tools;

public class ToolArgumentJsonOptionsTests
{
    [Fact]
    public void Default_大小写不敏感()
    {
        Assert.True(ToolArgumentJsonOptions.Default.PropertyNameCaseInsensitive);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ToolArgumentJsonOptionsTests"`
Expected: 编译失败 / FAIL，`ToolArgumentJsonOptions` 未定义。

- [ ] **Step 3: 写最小实现**

Create `src/ManInBlack.AI.Abstraction/Tools/ToolArgumentJsonOptions.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManInBlack.AI.Abstraction.Tools;

/// <summary>
/// 源生成器生成的 [AiTool] handler 反序列化 <c>JsonElement</c> 参数时使用的共享选项。
/// LLM 可能回传 camelCase 或 PascalCase，统一大小写不敏感匹配。
/// </summary>
public static class ToolArgumentJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ToolArgumentJsonOptionsTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ManInBlack.AI.Abstraction/Tools/ToolArgumentJsonOptions.cs test/ManInBlack.AI.Tests/Tools/ToolArgumentJsonOptionsTests.cs
git commit -m "✨ 新增 ToolArgumentJsonOptions：工具参数反序列化共享 JSON 选项"
```

---

## Task 2: 对象参数的 schema 生成

让 `BuildJsonSchema` 处理标量（保持与现状逐字节一致）+ 对象（`object`+公共属性）。emitter 的 `BuildParametersJsonSchema` 改用预生成的 `param.JsonSchema`。

**Files:**
- Modify: `src/ManInBlack.AI.SourceGenerator/ToolMethodModel.cs`
- Modify: `src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs`
- Modify: `src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs`
- Modify: `test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj`
- Create: `test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs`
- Test: `test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs`

- [ ] **Step 1: 让测试项目引用源生成器（使 `[AiTool]` 生成 handler）**

Modify `test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj`，在 `<ItemGroup>`（含两个 `ProjectReference` 的那个）里加一条：

```xml
    <ProjectReference Include="..\..\src\ManInBlack.AI.SourceGenerator\ManInBlack.AI.SourceGenerator.csproj" OutputItemType="Analyzer" />
```

> 说明：`OutputItemType="Analyzer"` 让源生成器在测试项目里跑；不写 `ReferenceOutputAssembly="false"`，使得 Task 7 里能 `new ToolCallerGenerator()`。若 Task 7 编译报找不到 `ToolCallerGenerator` 类型，再补一条普通 `<ProjectReference Include="..\..\src\ManInBlack.AI.SourceGenerator\ManInBlack.AI.SourceGenerator.csproj" />`（不带 `OutputItemType`）。

- [ ] **Step 2: 写测试用的复杂参数类型 + 测试工具**

Create `test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs`:

```csharp
using ManInBlack.AI.Abstraction.Attributes;

namespace ManInBlack.AI.Tests.Tools;

/// <summary>测试用复杂对象参数类型。</summary>
public class ChoiceOption
{
    public string Label { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>
/// 承载复杂参数的工具，供生成器/运行时测试。partial 供源生成器生成 handler。
/// 不标 [ServiceRegister]，测试里手动 AddScoped 注册。
/// </summary>
public partial class ComplexParamsTestTools
{
    public ChoiceOption? LastOption { get; private set; }

    /// <summary>选一个选项。</summary>
    /// <param name="option">单个选项对象</param>
    /// <returns>所选 label</returns>
    [AiTool]
    public string PickOne(ChoiceOption option)
    {
        LastOption = option;
        return option.Label;
    }
}
```

- [ ] **Step 3: 写失败测试（schema：对象 → object + 公共属性 + required）**

Create `test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs`:

```csharp
using System.Text.Json;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Tools;

public class AiToolComplexParamsTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddToolHandlers();                 // 测试项目生成的 internal 扩展（同程序集可见）
        services.AddScoped<ComplexParamsTestTools>();
        return services.BuildServiceProvider();
    }

    private static AIFunctionDeclaration GetDecl(string toolName)
    {
        using var sp = BuildSp();
        var registry = sp.GetRequiredService<ToolRegistry>();
        return registry.GetAll().First(d => d.Name == toolName);
    }

    [Fact]
    public void Schema_对象参数_生成object与公共属性()
    {
        var decl = GetDecl("PickOne");
        var option = decl.JsonSchema.GetProperty("properties").GetProperty("option");

        Assert.Equal("object", option.GetProperty("type").GetString());
        var props = option.GetProperty("properties");
        Assert.Contains("label", props.EnumerateObject().Select(p => p.Name));
        Assert.Contains("description", props.EnumerateObject().Select(p => p.Name));
        // label 非可空 → required；description 可空 → 不 required
        var required = option.GetProperty("required").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Contains("label", required);
        Assert.DoesNotContain("description", required);
    }
}
```

- [ ] **Step 4: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolComplexParamsTests.Schema_对象参数"`
Expected: FAIL（当前 schema 把 `option` 生成成 `"type":"object"` 但无 `properties`/`required`，断言失败）。若因 `AddToolHandlers` 找不到而编译失败，确认 Step 1 的 csproj 改动已保存。

- [ ] **Step 5: `ToolParameterModel` 加字段**

Modify `src/ManInBlack.AI.SourceGenerator/ToolMethodModel.cs`，在 `ToolParameterModel` 里加三个属性：

```csharp
    public string? JsonSchema { get; set; }            // 预生成的参数 JSON Schema 字符串
    public bool IsUnsupportedType { get; set; }        // 类型不受支持（触发 MIB014）
    public string? UnsupportedReason { get; set; }     // 不支持原因（诊断消息）
```

- [ ] **Step 6: 源生成器端递归生成 schema（标量 + 对象）**

Modify `src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs`：

(a) 在 `GetToolMethodModel` 里，把 XML 文档提取（`ExtractXmlDoc`）**移到参数构造之前**，先拿到 `paramDescriptions`，再构造参数，并为每个参数计算 `JsonSchema`。把现有的 `parameters = methodSymbol.Parameters.Select(p => new ToolParameterModel {...}).ToList();` 改为：

```csharp
        var (summary, paramDescriptions, returnsDescription) = ExtractXmlDoc(methodDecl);

        var parameters = methodSymbol.Parameters.Select(p =>
        {
            var model = new ToolParameterModel
            {
                Name = p.Name,
                Type = p.Type.ToDisplayString(fullyQualifiedFormat),
                FullTypeName = p.Type.ToDisplayString(fullyQualifiedFormat),
                IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated ||
                             p.Type.NullableAnnotation == NullableAnnotation.Annotated,
                IsValueType = p.Type.IsValueType,
                HasDefaultValue = p.HasExplicitDefaultValue,
                DefaultValueExpr = p.HasExplicitDefaultValue
                    ? FormatDefaultValue(p.ExplicitDefaultValue, p.Type)
                    : null,
            };
            paramDescriptions.TryGetValue(p.Name, out var desc);
            model.JsonSchema = BuildJsonSchema(p.Type, desc, isUnsupported: out var unsupported, unsupportedReason: out var reason);
            model.IsUnsupportedType = unsupported;
            model.UnsupportedReason = reason;
            return model;
        }).ToList();
```

并删除原先下面那行 `var (summary, paramDescriptions, returnsDescription) = ExtractXmlDoc(methodDecl);`（避免重复定义）。

(b) 在 `ToolCallerGenerator` 类内（`FormatDefaultValue` 附近）新增静态方法（标量分支刻意与 emitter 旧实现逐字节一致，避免回归）：

```csharp
    private const int MaxSchemaDepth = 4;

    /// <summary>递归构造参数 JSON Schema 字符串。isUnsupported/unsupportedReason 由引用参数回传给调用方做诊断。</summary>
    private static string BuildJsonSchema(
        ITypeSymbol type, string? description,
        out bool isUnsupported, out string? unsupportedReason, int depth = 0)
    {
        isUnsupported = false;
        unsupportedReason = null;

        var (effective, isNullable) = UnwrapNullable(type);

        // 标量
        if (ScalarInfo(effective) is var (scalarType, format) && scalarType is not null)
            return ScalarJson(scalarType, format, isNullable, description);

        // enum
        if (effective.TypeKind == TypeKind.Enum)
            return EnumJson(effective, isNullable, description);

        // 数组 / 白名单集合（元素是否受支持由 CollectionJson 内的递归 BuildJsonSchema 回传）
        if (TryGetCollectionElement(effective) is { } elementType)
            return CollectionJson(elementType, isNullable, description, depth,
                out isUnsupported, out unsupportedReason);

        // 深度上限：降级为不透明 object
        if (depth >= MaxSchemaDepth)
            return OpaqueObjectJson(isNullable, description);

        // 受支持的对象（POCO / record）
        if (effective is INamedTypeSymbol named && IsSupportedObjectType(named))
            return ObjectJson(named, isNullable, description, depth);

        // 其余类型不支持
        isUnsupported = true;
        unsupportedReason = $"类型 '{effective.ToDisplayString()}' 不受支持";
        return OpaqueObjectJson(isNullable, description);
    }
```

(c) 新增上述方法引用的辅助方法（同一文件、同类）：

```csharp
    private static (ITypeSymbol effective, bool isNullable) UnwrapNullable(ITypeSymbol type)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type is INamedTypeSymbol n && n.TypeArguments.Length == 1)
            return (n.TypeArguments[0], true);
        var isNullable = type.IsReferenceType &&
                         type.NullableAnnotation == NullableAnnotation.Annotated;
        return (isNullable ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) : type, isNullable);
    }

    /// <returns>(jsonType, format)；非标量返回 (null, null)。</returns>
    private static (string? type, string? format) ScalarInfo(ITypeSymbol t)
    {
        switch (t.SpecialType)
        {
            case SpecialType.System_Boolean: return ("boolean", null);
            case SpecialType.System_String:
            case SpecialType.System_Char: return ("string", null);
            case SpecialType.System_DateTime: return ("string", "date-time");
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal: return ("number", null);
            case SpecialType.System_Byte: case SpecialType.System_SByte:
            case SpecialType.System_Int16: case SpecialType.System_UInt16:
            case SpecialType.System_Int32: case SpecialType.System_UInt32:
            case SpecialType.System_Int64: case SpecialType.System_UInt64: return ("integer", null);
            default: break;
        }
        // DateTimeOffset 非内置 SpecialType，按名兜底
        var fqn = t.ToDisplayString();
        if (fqn is "System.DateTimeOffset" or "DateTimeOffset") return ("string", "date-time");
        return (null, null);
    }

    private static string ScalarJson(string type, string? format, bool isNullable, string? description)
    {
        var sb = new System.Text.StringBuilder("{");
        sb.Append(isNullable ? $"\"type\":[\"{type}\",\"null\"]" : $"\"type\":\"{type}\"");
        if (format is not null) sb.Append($",\"format\":\"{format}\"");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string EnumJson(ITypeSymbol enumType, bool isNullable, string? description)
    {
        var names = enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.ConstantValue is not null)
            .Select(f => EscapeJson(f.Name));
        var values = string.Join(",", names);
        var sb = new System.Text.StringBuilder("{");
        sb.Append(isNullable ? $"\"type\":[\"string\",\"null\"]" : "\"type\":\"string\"");
        sb.Append($",\"enum\":[{values}]");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string OpaqueObjectJson(bool isNullable, string? description)
    {
        var sb = new System.Text.StringBuilder("{");
        sb.Append(isNullable ? "\"type\":[\"object\",\"null\"]" : "\"type\":\"object\"");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string CollectionJson(
        ITypeSymbol elementType, bool isNullable, string? description, int depth,
        out bool isUnsupported, out string? unsupportedReason)
    {
        var itemSchema = BuildJsonSchema(elementType, null, out isUnsupported, out unsupportedReason, depth + 1);
        var sb = new System.Text.StringBuilder("{");
        sb.Append(isNullable ? "\"type\":[\"array\",\"null\"]" : "\"type\":\"array\"");
        sb.Append($",\"items\":{itemSchema}");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string ObjectJson(INamedTypeSymbol type, bool isNullable, string? description, int depth)
    {
        var props = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.GetMethod is not null &&
                        p.GetMethod.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        var sb = new System.Text.StringBuilder("{\"type\":");
        sb.Append(isNullable ? "[\"object\",\"null\"]" : "\"object\"");
        sb.Append(",\"properties\":{");
        for (var i = 0; i < props.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var p = props[i];
            sb.Append($"\"{EscapeJson(p.Name)}\":");
            sb.Append(BuildJsonSchema(p.Type, null, out _, out _, depth + 1));
        }
        sb.Append('}');

        var required = props
            .Where(p => p.NullableAnnotation != NullableAnnotation.Annotated)
            .Select(p => $"\"{EscapeJson(p.Name)}\"").ToList();
        if (required.Count > 0)
        {
            sb.Append(",\"required\":[");
            sb.Append(string.Join(",", required));
            sb.Append(']');
        }
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static ITypeSymbol? TryGetCollectionElement(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr) return arr.ElementType;
        if (type is INamedTypeSymbol named)
        {
            var def = named.ConstructedFrom.ToDisplayString();
            if (s_CollectionDefs.Contains(def) && named.TypeArguments.Length == 1)
                return named.TypeArguments[0];
        }
        return null;
    }

    private static readonly System.Collections.Generic.HashSet<string> s_CollectionDefs =
    [
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
        "System.Collections.Generic.IEnumerable<T>",
        "System.Collections.Generic.HashSet<T>",
        "System.Collections.Generic.ISet<T>",
        "System.Collections.Generic.IReadOnlySet<T>",
        "System.Collections.Generic.Queue<T>",
        "System.Collections.Generic.Stack<T>",
        "System.Collections.Generic.LinkedList<T>",
    ];

    /// <summary>受支持的对象类型：非Dictionary、非tuple、封闭泛型外的 class/struct。</summary>
    private static bool IsSupportedObjectType(INamedTypeSymbol t)
    {
        if (t.IsTupleType) return false;
        if (t.TypeArguments.Length > 0 && t.TypeParameters.Length > 0 &&
            t.TypeArguments.Any(a => a.Kind == SymbolKind.TypeParameter)) return false; // 开放泛型
        var def = t.ConstructedFrom.ToDisplayString();
        if (def.StartsWith("System.Collections.Generic.Dictionary") ||
            def.StartsWith("System.Collections.Generic.IDictionary") ||
            def.StartsWith("System.Collections.Generic.IReadOnlyDictionary") ||
            def == "System.Object" || def == "object")
            return false;
        return t.TypeKind is TypeKind.Class or TypeKind.Struct;
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
```

> 注意：`BuildJsonSchema` 用的是 `System.Text.StringBuilder`（普通运行时代码，**不是**生成器代码生成路径），不违反「源生成器禁止原始 StringBuilder」——那条规则针对的是**用 EasyCodeBuilder 拼源码**的场景。这里是在生成器进程内构造运行时用的 JSON 字符串，用普通 `StringBuilder` 合规。

- [ ] **Step 7: emitter 改用 `param.JsonSchema`**

Modify `src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs` 的 `BuildParametersJsonSchema`：把 `sb.Append(BuildTypeSchemaJson(param.Type, param.IsNullable, desc));`（连同上面取 `desc` 的那行）替换为直接插入预生成 schema：

```csharp
    private static string BuildParametersJsonSchema(ToolMethodModel tool)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"object\",\"properties\":{");

        var firstProp = true;
        foreach (var param in tool.Parameters)
        {
            if (!firstProp) sb.Append(',');
            firstProp = false;

            sb.Append($"\"{EscapeJsonString(param.Name)}\":");
            sb.Append(param.JsonSchema);   // 由 ToolCallerGenerator.BuildJsonSchema 预生成
        }

        sb.Append('}');

        var requiredParams = tool.Parameters
            .Where(p => !p.IsNullable && !p.HasDefaultValue)
            .Select(p => p.Name)
            .ToList();

        if (requiredParams.Count > 0)
        {
            sb.Append(",\"required\":[");
            for (var i = 0; i < requiredParams.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"\"{EscapeJsonString(requiredParams[i])}\"");
            }
            sb.Append(']');
        }

        sb.Append('}');
        return sb.ToString();
    }
```

> `BuildTypeSchemaJson` / `MapToJsonSchemaType` / `GetFormat` 暂时保留（返回值 schema 与 Task 4 的 enum 仍用）。

- [ ] **Step 8: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolComplexParamsTests.Schema_对象参数"`
Expected: PASS。

- [ ] **Step 9: 回归——现有标量工具 schema 不变**

Run: `dotnet build src/ManInBlack.AI`
Expected: 成功（确认标量参数 schema 逐字节未变、生成器无诊断）。若失败，检查 `ScalarJson` 输出是否与旧 `BuildTypeSchemaJson` 完全一致（nullable/format/description 顺序）。

- [ ] **Step 10: 提交**

```bash
git add src/ManInBlack.AI.SourceGenerator/ToolMethodModel.cs src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs
git commit -m "✨ [AiTool] 支持对象参数：generator 递归生成 object schema"
```

---

## Task 3: 数组 / 集合参数的 schema 生成

`TryGetCollectionElement` + `CollectionJson` 已在 Task 2 落地，本任务加测试覆盖并确保 `List<T>`/`T[]` 生效。

**Files:**
- Modify: `test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs`（加 `PickMany`）
- Test: `test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs`

- [ ] **Step 1: 测试工具加集合/数组方法**

在 `ComplexParamsTestTools`（`ComplexParamsTestTools.cs`）加两个方法：

```csharp
    public List<ChoiceOption>? LastList { get; private set; }
    public ChoiceOption[]? LastArray { get; private set; }

    /// <summary>选多个选项。</summary>
    /// <param name="options">选项列表</param>
    /// <returns>所选数量</returns>
    [AiTool]
    public string PickMany(List<ChoiceOption> options)
    {
        LastList = options;
        return options.Count.ToString();
    }

    /// <summary>按数组选。</summary>
    /// <param name="options">选项数组</param>
    /// <returns>所选数量</returns>
    [AiTool]
    public string PickFromArray(ChoiceOption[] options)
    {
        LastArray = options;
        return options.Length.ToString();
    }
```

- [ ] **Step 2: 写失败测试（schema：集合 → array + items）**

在 `AiToolComplexParamsTests.cs` 加：

```csharp
    [Theory]
    [InlineData("PickMany")]
    [InlineData("PickFromArray")]
    public void Schema_集合参数_生成array与items(string toolName)
    {
        var decl = GetDecl(toolName);
        var options = decl.JsonSchema.GetProperty("properties").GetProperty("options");

        Assert.Equal("array", options.GetProperty("type").GetString());
        Assert.Equal("object", options.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("string", options.GetProperty("items").GetProperty("properties").GetProperty("label").GetProperty("type").GetString());
    }
```

- [ ] **Step 3: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolComplexParamsTests.Schema_集合参数"`
Expected: PASS（Task 2 已实现 `CollectionJson`）。若 `PickFromArray` 的 array schema 缺失，确认 `TryGetCollectionElement` 的 `IArrayTypeSymbol` 分支生效。

- [ ] **Step 4: 提交**

```bash
git add test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs
git commit -m "✅ [AiTool] 补集合/数组参数 schema 测试覆盖"
```

---

## Task 4: enum 参数的 schema 生成

schema 端（generator 的 `EnumJson`，Task 2 已实现）+ emitter 返回值 schema 的 `MapToJsonSchemaType` enum 分支。

**Files:**
- Modify: `test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs`（加 `Color` + `SetColor`）
- Modify: `src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs`（`MapToJsonSchemaType`）
- Test: `test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs`

- [ ] **Step 1: 测试工具加 enum**

在 `ComplexParamsTestTools.cs` 顶部（namespace 内）加：

```csharp
public enum Color { Red, Green, Blue }
```

在 `ComplexParamsTestTools` 加：

```csharp
    public Color LastColor { get; private set; }

    /// <summary>设置颜色。</summary>
    /// <param name="color">颜色枚举</param>
    /// <returns>所选颜色名</returns>
    [AiTool]
    public string SetColor(Color color)
    {
        LastColor = color;
        return color.ToString();
    }
```

- [ ] **Step 2: 写失败测试（schema：enum → string + enum 值）**

在 `AiToolComplexParamsTests.cs` 加：

```csharp
    [Fact]
    public void Schema_enum参数_生成string与枚举值()
    {
        var decl = GetDecl("SetColor");
        var color = decl.JsonSchema.GetProperty("properties").GetProperty("color");

        Assert.Equal("string", color.GetProperty("type").GetString());
        var values = color.GetProperty("enum").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(new[] { "Red", "Green", "Blue" }, values);
    }
```

- [ ] **Step 3: 运行测试确认通过（参数侧）**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolComplexParamsTests.Schema_enum"`
Expected: PASS（`EnumJson` 在 Task 2 已实现）。

- [ ] **Step 4: 返回值 schema 也改由 generator 生成（让 enum 返回值受益）**

`MapToJsonSchemaType` 只拿到类型名字符串、无法判 enum；最干净的做法是把返回值 schema 也由 generator 用 `ITypeSymbol` 预生成（与参数一致）。改动三处：

(a) `ToolMethodModel.cs` 加字段，紧邻 `ReturnType`：

```csharp
    public string? ReturnJsonSchema { get; set; }
```

(b) `ToolCallerGenerator.cs` 的 `UnwrapAsyncReturnType` 改为同时返回符号。整体替换为：

```csharp
    private static (bool isAsync, ITypeSymbol returnTypeSymbol, string returnType, bool returnsVoid) UnwrapAsyncReturnType(
        ITypeSymbol returnType, SymbolDisplayFormat format)
    {
        if (returnType is not INamedTypeSymbol named)
            return (false, returnType, returnType.ToDisplayString(format), returnType.SpecialType == SpecialType.System_Void);

        if (!IsTaskType(named))
            return (false, returnType, returnType.ToDisplayString(format), returnType.SpecialType == SpecialType.System_Void);

        if (named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var innerType = named.TypeArguments[0];
            return (true, innerType, innerType.ToDisplayString(format), false);
        }

        return (true, returnType, "void", true);
    }
```

把其调用处（`var (isAsync, actualReturnType, returnsVoid) = UnwrapAsyncReturnType(methodSymbol.ReturnType, fullyQualifiedFormat);`）改为解构四元组：

```csharp
        var (isAsync, actualReturnSymbol, actualReturnType, returnsVoid) = UnwrapAsyncReturnType(methodSymbol.ReturnType, fullyQualifiedFormat);
```

并在 `return new ToolMethodModel { ... }` 块之前加：

```csharp
        string? returnJsonSchema = null;
        if (!returnsVoid)
            returnJsonSchema = BuildJsonSchema(actualReturnSymbol, returnsDescription, out _, out _);
```

在该 `return new ToolMethodModel { ... }` 对象初始化里加一项 `ReturnJsonSchema = returnJsonSchema,`（`ReturnType = actualReturnType` 保持不变）。

(c) `ToolCallerEmitter.cs` 的 `BuildDeclarationExpression` 把：

```csharp
        var returnSchema = BuildReturnJsonSchema(tool);
```

改为：

```csharp
        var returnSchema = tool.ReturnJsonSchema;
```

然后删除现已无引用的 `BuildReturnJsonSchema`、`BuildTypeSchemaJson`、`MapToJsonSchemaType`、`GetFormat` 四个方法（保持 emitter 整洁）。

> 标量返回值（如 `Task<string>`）经 `BuildJsonSchema` 标量分支产出与旧实现逐字节一致的 `{"type":"string",...}`；`void` 返回 `null`。无回归。

- [ ] **Step 5: 重新构建 + 运行全部 schema 测试**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolComplexParamsTests"`
Expected: 全部 PASS。

- [ ] **Step 6: 提交**

```bash
git add src/ManInBlack.AI.SourceGenerator/ToolMethodModel.cs src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs
git commit -m "✨ [AiTool] 支持 enum 参数：generator 生成 string+enum schema；返回值 schema 统一走 generator"
```

---

## Task 5: 运行时反序列化（核心）

修 `ConvertExpr` 引用类型分支，使 `JsonElement` 参数被反序列化为声明类型。

**Files:**
- Modify: `src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs`
- Test: `test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs`

- [ ] **Step 1: 写失败测试（运行时反序列化 + 大小写兼容 + enum）**

在 `AiToolComplexParamsTests.cs` 加辅助与用例：

```csharp
    private static async Task<(object? Result, Exception? Error)> ExecuteAsync(
        string toolName, IDictionary<string, object?> arguments)
    {
        var sp = BuildSp();
        using var scope = sp.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IToolExecutor>();
        var ctx = new ToolExecuteContext(scope.ServiceProvider)
        {
            ToolName = toolName,
            CallId = "c1",
            Arguments = arguments,
        };
        await executor.ExecuteAsync(ctx, default);
        return (ctx.Result, ctx.Error);
    }

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task 运行时_对象参数_反序列化PascalCase()
    {
        var (result, error) = await ExecuteAsync("PickOne",
            new Dictionary<string, object?> { ["option"] = El("""{"Label":"A","Description":"x"}""") });
        Assert.Null(error);
        Assert.Equal("A", result);
    }

    [Fact]
    public async Task 运行时_对象参数_反序列化camelCase()
    {
        var (result, error) = await ExecuteAsync("PickOne",
            new Dictionary<string, object?> { ["option"] = El("""{"label":"B","description":"y"}""") });
        Assert.Null(error);
        Assert.Equal("B", result);
    }

    [Fact]
    public async Task 运行时_集合参数_反序列化()
    {
        var (result, error) = await ExecuteAsync("PickMany",
            new Dictionary<string, object?> { ["options"] = El("""[{"label":"A"},{"label":"B"}]""") });
        Assert.Null(error);
        Assert.Equal("2", result);
    }

    [Theory]
    [InlineData("\"Green\"", "Green")]
    [InlineData("1", "Green")]   // 数字也兼容
    public async Task 运行时_enum参数_反序列化(string jsonValue, string expected)
    {
        var (result, error) = await ExecuteAsync("SetColor",
            new Dictionary<string, object?> { ["color"] = El(jsonValue) });
        Assert.Null(error);
        Assert.Equal(expected, result);
    }
```

需在文件头加 `using ManInBlack.AI.Abstraction.Tools;`（`ToolExecuteContext`/`IToolExecutor`）与 `using System.Text.Json;`。

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolComplexParamsTests.运行时"`
Expected: FAIL（当前 `value as ChoiceOption` → null → `option.Label` NRE，`ctx.Error` 非空）。

- [ ] **Step 3: 改 `ConvertExpr` 引用类型分支 + 加 using**

Modify `src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs`：

(a) `Emit()` 的 `.Using(...)` 列表里加 `"System.Text.Json"`。

(b) `ConvertExpr` 末尾的引用类型分支：

```csharp
        return $"{varName} as {targetType}";
```

替换为：

```csharp
        return $"{varName} is {targetType} {varName}_v ? {varName}_v"
             + $" : ({varName} is System.Text.Json.JsonElement {varName}_je"
             + $" ? {varName}_je.Deserialize<{targetType}>(ToolArgumentJsonOptions.Default)"
             + $" : default({targetType}))";
```

> emiter 已 `using ManInBlack.AI.Abstraction.Tools;`（见 `Emit()` 的 Using 列表），`ToolArgumentJsonOptions` 可直接引用。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolComplexParamsTests.运行时"`
Expected: 全部 PASS。

- [ ] **Step 5: 回归——现有标量工具调用不受影响**

Run: `dotnet build ManInBlack.slnx`
Expected: 成功。

- [ ] **Step 6: 提交**

```bash
git add src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs
git commit -m "🐛 [AiTool] 复杂参数运行时反序列化：ConvertExpr 引用类型分支改用 JsonElement.Deserialize"
```

---

## Task 6: 递归深度上限

`MaxSchemaDepth = 4` 与 `OpaqueObjectJson` 降级已在 Task 2 落地；本任务加自引用/深嵌套测试，验证不报错、不无限递归、第 4 层降级。

**Files:**
- Modify: `test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs`（加 `Node` + `Walk`）
- Test: `test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs`

- [ ] **Step 1: 测试工具加自引用类型**

在 `ComplexParamsTestTools.cs`（namespace 内）加：

```csharp
/// <summary>自引用类型，用于验证 schema 深度上限。</summary>
public class Node
{
    public string Name { get; set; } = "";
    public Node? Child { get; set; }
}
```

在 `ComplexParamsTestTools` 加：

```csharp
    /// <summary>遍历节点。</summary>
    /// <param name="root">根节点（自引用）</param>
    /// <returns>根节点名</returns>
    [AiTool]
    public string Walk(Node root) => root.Name;
```

- [ ] **Step 2: 写测试（深度上限：不无限递归、第 4 层降级为无 properties）**

在 `AiToolComplexParamsTests.cs` 加：

```csharp
    [Fact]
    public void Schema_自引用类型_深度上限内降级不无限递归()
    {
        var decl = GetDecl("Walk");
        var root = decl.JsonSchema.GetProperty("properties").GetProperty("root");

        // 顶层 Node 有 properties（name/child）
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("properties").TryGetProperty("child", out _));

        // 逐层下钻 child，第 MaxSchemaDepth(4) 层起应为不透明 object（无 properties）
        var current = root.GetProperty("properties").GetProperty("child");
        for (var i = 0; i < 4; i++)
        {
            if (current.TryGetProperty("properties", out var props) &&
                props.TryGetProperty("child", out var next))
            {
                current = next;
                continue;
            }
            break; // 已降级为不透明 object
        }
        // 走到降级层：type 仍是 object，但无 properties
        Assert.Equal("object", current.GetProperty("type").GetString());
        Assert.False(current.TryGetProperty("properties", out _));
    }
```

- [ ] **Step 3: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolComplexParamsTests.Schema_自引用"`
Expected: PASS。若卡死/栈溢出，说明 `ObjectJson` 递归未在 `BuildJsonSchema` 入口判 `depth >= MaxSchemaDepth`，检查 Task 2 Step 6 的 `BuildJsonSchema` 深度判断顺序（深度判断须在 `ObjectJson` 之前）。

- [ ] **Step 4: 提交**

```bash
git add test/ManInBlack.AI.Tests/Tools/ComplexParamsTestTools.cs test/ManInBlack.AI.Tests/Tools/AiToolComplexParamsTests.cs
git commit -m "✅ [AiTool] 补自引用类型 schema 深度上限测试"
```

---

## Task 7: 不受支持类型报错（MIB014）

新增 MIB014（Error）诊断：字典/tuple/open generic/`object` 等参数 → 编译报错。`IsUnsupportedType` 字段已在 Task 2 由 `BuildJsonSchema` 回传；本任务接上诊断上报 + 写 Roslyn driver 测试。

**Files:**
- Modify: `src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs`（MIB014 描述符 + `ReportDiagnostics` 上报）
- Create: `test/ManInBlack.AI.Tests/Tools/GeneratorDriverHelper.cs`
- Create: `test/ManInBlack.AI.Tests/Tools/AiToolUnsupportedParamTests.cs`

- [ ] **Step 1: 写 Roslyn driver 测试助手**

Create `test/ManInBlack.AI.Tests/Tools/GeneratorDriverHelper.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ManInBlack.AI.SourceGenerator;        // 需 SG 作为普通引用（见 Task 2 Step 1 备注）
using ManInBlack.AI.Abstraction.Attributes; // 触发加载 Abstraction 程序集，供引用解析

namespace ManInBlack.AI.Tests.Tools;

/// <summary>用 Roslyn CSharpGeneratorDriver 直接跑源生成器，用于诊断/生成源测试。</summary>
public static class GeneratorDriverHelper
{
    public static GeneratorDriver Run(string source)
    {
        _ = typeof(AiToolAttribute).Assembly; // 确保 Abstraction 程序集已加载

        var parseOptions = CSharpParseOptions.Default;
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDriverTest",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: GetReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ToolCallerGenerator(), parseOptions: parseOptions);
        return driver.RunGenerators(compilation);
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                yield return MetadataReference.CreateFromFile(asm.Location);
        }
    }
}
```

> 若 `ToolCallerGenerator` 类型解析不到：在测试 csproj 再加一条普通 `<ProjectReference Include="..\..\src\ManInBlack.AI.SourceGenerator\ManInBlack.AI.SourceGenerator.csproj" />`（不带 `OutputItemType`）。

- [ ] **Step 2: 写失败测试（Dictionary 参数 → MIB014）**

Create `test/ManInBlack.AI.Tests/Tools/AiToolUnsupportedParamTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Xunit;

namespace ManInBlack.AI.Tests.Tools;

public class AiToolUnsupportedParamTests
{
    private const string Source = """
using System.Collections.Generic;
using ManInBlack.AI.Abstraction.Attributes;
namespace TestNs;
public partial class BadTools
{
    /// <summary>bad</summary>
    /// <param name="map">dict</param>
    /// <returns>x</returns>
    [AiTool]
    public string DoStuff(Dictionary<string, string> map) => "x";
}
""";

    [Fact]
    public void Dictionary参数_报MIB014错误()
    {
        var result = GeneratorDriverHelper.Run(Source).GetRunResult();
        Assert.Contains(result.Diagnostics, d => d.Id == "MIB014" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void 对象参数_不报MIB014()
    {
        var supported = """
using ManInBlack.AI.Abstraction.Attributes;
namespace TestNs;
public class Opt { public string Label { get; set; } = ""; }
public partial class GoodTools
{
    /// <summary>ok</summary>
    /// <param name="o">opt</param>
    /// <returns>x</returns>
    [AiTool]
    public string DoStuff(Opt o) => o.Label;
}
""";
        var result = GeneratorDriverHelper.Run(supported).GetRunResult();
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "MIB014");
    }
}
```

- [ ] **Step 3: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolUnsupportedParamTests"`
Expected: FAIL（`Dictionary参数` 用例找不到 MIB014）。

- [ ] **Step 4: 加 MIB014 描述符 + 上报**

Modify `src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs`：

(a) 在类顶部诊断描述符区（`MissingReturnsDoc` 之后）加：

```csharp
    private static readonly DiagnosticDescriptor UnsupportedParameterType = new(
        id: "MIB014",
        title: "[AiTool] 参数类型不受源生成器支持",
        messageFormat: "[AiTool] 方法 '{0}' 的参数 '{1}' 类型 '{2}' 不受支持，请改用标量/enum/对象/数组/集合（{3}）",
        category: "AiToolDeclaration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
```

(b) 在 `ReportDiagnostics` 的 `foreach (var method in methods)` 循环里（MIB012 之后、MIB013 之前或之后均可）加：

```csharp
            // MIB014: 不受支持的参数类型
            foreach (var param in method.Parameters)
            {
                if (param.IsUnsupportedType)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedParameterType,
                        null,
                        method.MethodName,
                        param.Name,
                        param.Type,
                        param.UnsupportedReason ?? "未知原因"));
                }
            }
```

> 因 MIB014 是 Error，带 Dictionary 参数的程序集编译会失败——这正是预期（杜绝静默坏掉）。生成的 handler 代码对不支持参数仍走 `as T` 兜底（合法 C#），不影响其它工具。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AiToolUnsupportedParamTests"`
Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs test/ManInBlack.AI.Tests/Tools/GeneratorDriverHelper.cs test/ManInBlack.AI.Tests/Tools/AiToolUnsupportedParamTests.cs
git commit -m "🚫 [AiTool] 新增 MIB014：不受支持的参数类型编译报错"
```

---

## Task 8: 更新文档

**Files:**
- Modify: `docs/sourcegenerator-guide.md`
- Modify: `docs/tools-guide.md`

- [ ] **Step 1: sourcegenerator-guide.md 加 MIB014 + 复杂参数说明**

在诊断规则表加一行：

```markdown
| MIB014 | Error    | `[AiTool]` 参数类型不受支持（字典/tuple/open generic/`object` 等） |
```

在表后补一小节「复杂参数类型支持」：

```markdown
## 复杂参数类型支持

`[AiTool]` 方法参数支持标量、enum、对象（POCO/record，取公共可读实例属性）、数组与集合。
schema 由源生成器从 Roslyn `ITypeSymbol` 递归生成；运行时由生成的 handler 用
`JsonElement.Deserialize<T>(ToolArgumentJsonOptions.Default)` 反序列化（大小写不敏感）。

受支持集合：`T[]`、`List<T>`、`IList<T>`、`ICollection<T>`、`IReadOnlyList<T>`、
`IReadOnlyCollection<T>`、`IEnumerable<T>`、`HashSet<T>`、`ISet<T>`、`IReadOnlySet<T>`、
`Queue<T>`、`Stack<T>`、`LinkedList<T>`。

嵌套 schema 深度上限 4，超出降级为不透明 `object`（防自引用死循环）。
`Dictionary<,>`、元组、开放泛型、`object` 等不受支持 → MIB014 报错。
```

- [ ] **Step 2: tools-guide.md 补复杂参数用法**

在「编写自定义工具」节加示例：

```markdown
### 复杂对象/数组参数

工具参数可使用对象或集合，源生成器自动生成嵌套 JSON Schema 并在运行时反序列化：

\`\`\`csharp
public class ChoiceOption
{
    /// <summary>选项文案</summary>
    public string Label { get; set; } = "";
    /// <summary>选项说明（可选）</summary>
    public string? Description { get; set; }
}

[AiTool]
public string Ask(string question, List<ChoiceOption> options) => ...;
\`\`\`

> 对象成员取**公共可读属性**；属性上的 XML 文档注释不进 schema（仅方法参数的 `<param>` 进 schema 描述）。
```

- [ ] **Step 3: 提交**

```bash
git add docs/sourcegenerator-guide.md docs/tools-guide.md
git commit -m "📝 文档：补充 [AiTool] 复杂参数支持与 MIB014"
```

---

## Task 9: 全量回归

- [ ] **Step 1: 全量构建**

Run: `dotnet build ManInBlack.slnx`
Expected: 成功，无 MIB014（现有工具无字典/tuple 参数）。

- [ ] **Step 2: 全量测试**

Run: `dotnet test`
Expected: 全绿。

- [ ] **Step 3: 抽查 demo 构建**

Run: `dotnet build demo/AgentConsole && dotnet build demo/FeishuAdaptor`
Expected: 成功。

- [ ] **Step 4: （可选）提交回归标记**

若无改动则跳过；若有零星格式修正：

```bash
git commit --allow-empty -m "✅ 回归验证：[AiTool] 复杂参数支持全量构建+测试通过"
```
