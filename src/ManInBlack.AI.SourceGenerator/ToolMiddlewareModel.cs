using Microsoft.CodeAnalysis;

namespace ManInBlack.AI.SourceGenerator;

/// <summary>
/// 包含 [AiTool] 方法的类的模型，用于生成对应的 ToolInjection Middleware
/// </summary>
public sealed class ToolMiddlewareModel
{
    /// <summary>
    /// 类的短名称（不含命名空间），如 CommandLineTools
    /// </summary>
    public string ContainingTypeName { get; set; } = "";

    /// <summary>
    /// 类的全限定名称，如 ManInBlack.AI.Tools.CommandLineTools
    /// </summary>
    public string FullyQualifiedTypeName { get; set; } = "";

    /// <summary>
    /// 类所在的命名空间，如 ManInBlack.AI.Tools
    /// </summary>
    public string ContainingNamespace { get; set; } = "";

    /// <summary>
    /// 所属类是否声明为 partial（只有 partial 类才会生成 AllToolDeclarations）
    /// </summary>
    public bool IsPartialClass { get; set; }
}
