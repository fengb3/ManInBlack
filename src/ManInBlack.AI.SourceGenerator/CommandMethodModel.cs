using System.Collections.Generic;

namespace ManInBlack.AI.SourceGenerator;

/// <summary>扫描到的 [SlashCommand] 方法的模型数据。</summary>
public sealed class CommandMethodModel
{
    public string MethodName { get; set; } = "";
    public string ContainingTypeName { get; set; } = "";        // 全称,用于代码生成
    public string ContainingTypeShortName { get; set; } = "";   // 短名,用于错误信息
    public string CommandName { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public bool IsPartialClass { get; set; }
}
