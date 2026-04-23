using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ManInBlack.AI.SourceGenerator;

/// <summary>
/// [AiTool] 方法的声明模型，包含 XML 文档信息
/// </summary>
public sealed class ToolDeclarationModel
{
    public string MethodName { get; set; } = "";
    public string ContainingTypeName { get; set; } = "";
    public string ContainingTypeShortName { get; set; } = "";
    public string ContainingNamespace { get; set; } = "";
    public bool IsStatic { get; set; }
    public bool IsAsync { get; set; }
    public bool ReturnsVoid { get; set; }
    public string ReturnType { get; set; } = "void";
    public List<ToolDeclarationParameterModel> Parameters { get; set; } = [];

    /// <summary>
    /// &lt;summary&gt; 内容
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// 参数名 → &lt;param&gt; 描述
    /// </summary>
    public Dictionary<string, string> ParamDescriptions { get; set; } = [];

    /// <summary>
    /// &lt;returns&gt; 内容
    /// </summary>
    public string? ReturnsDescription { get; set; }

    /// <summary>
    /// 所属类是否声明为 partial
    /// </summary>
    public bool IsPartialClass { get; set; }

    /// <summary>
    /// 所属类是否为 static
    /// </summary>
    public bool IsStaticClass { get; set; }

    /// <summary>
    /// 诊断位置（类声明）
    /// </summary>
    public Location? ClassLocation { get; set; }

    /// <summary>
    /// 诊断位置（方法声明）
    /// </summary>
    public Location? MethodLocation { get; set; }

    /// <summary>
    /// 工具名（处理冲突后）
    /// </summary>
    public string ToolName { get; set; } = "";
}

public sealed class ToolDeclarationParameterModel
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsNullable { get; set; }
    public bool HasDefaultValue { get; set; }
}
