using System.Collections.Generic;

namespace ManInBlack.AI.SourceGenerator;

/// <summary>
/// 扫描到的 [AiTool] 方法的模型数据，同时包含执行信息和声明信息
/// </summary>
public sealed class ToolMethodModel
{
    public string MethodName { get; set; } = "";
    public string ContainingTypeName { get; set; } = "";        // 全称，用于代码生成
    public string ContainingTypeShortName { get; set; } = "";   // 短名，用于 ToolName 冲突解析
    public string FullyQualifiedTypeName { get; set; } = "";
    public string ContainingNamespace { get; set; } = "";
    public string ToolName { get; set; } = "";
    public bool IsStatic { get; set; }
    public bool IsAsync { get; set; }
    public bool ReturnsVoid { get; set; }
    public string ReturnType { get; set; } = "void";
    public List<ToolParameterModel> Parameters { get; set; } = [];
    public List<string> FilterTypes { get; set; } = [];

    // 声明信息（来自 XML 文档）
    public string? Summary { get; set; }
    public Dictionary<string, string> ParamDescriptions { get; set; } = [];
    public string? ReturnsDescription { get; set; }

    // 诊断信息
    public bool IsPartialClass { get; set; }
    public bool IsStaticClass { get; set; }
}

/// <summary>
/// [Tool] 方法参数的模型数据
/// </summary>
public sealed class ToolParameterModel
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string FullTypeName { get; set; } = "";
    public bool IsNullable { get; set; }
    public bool IsValueType { get; set; }
    public bool HasDefaultValue { get; set; }
    public string? DefaultValueExpr { get; set; }
    public string? JsonSchema { get; set; }            // 预生成的参数 JSON Schema 字符串
    public bool IsUnsupportedType { get; set; }        // 类型不受支持（触发 MIB014）
    public string? UnsupportedReason { get; set; }     // 不支持原因（诊断消息）
}
