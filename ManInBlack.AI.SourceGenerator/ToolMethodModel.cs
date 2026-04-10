using System.Collections.Generic;

namespace ManInBlack.AI.SourceGenerator;

/// <summary>
/// 扫描到的 [Tool] 方法的模型数据
/// </summary>
public sealed class ToolMethodModel
{
    public string MethodName { get; set; } = "";
    public string ContainingTypeName { get; set; } = "";        // 全称，用于代码生成
    public string ContainingTypeShortName { get; set; } = "";   // 短名，用于 ToolName 冲突解析
    public string FullyQualifiedTypeName { get; set; } = "";
    public string ToolName { get; set; } = "";
    public bool IsStatic { get; set; }
    public bool ReturnsVoid { get; set; }
    public string ReturnType { get; set; } = "void";
    public List<ToolParameterModel> Parameters { get; set; } = [];
    public List<string> FilterTypes { get; set; } = [];
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
}
