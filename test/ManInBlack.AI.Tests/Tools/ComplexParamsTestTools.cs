using ManInBlack.AI.Abstraction.Attributes;

namespace ManInBlack.AI.Tests.Tools;

/// <summary>测试用复杂对象参数类型。</summary>
public class ChoiceOption
{
    public string Label { get; set; } = "";
    public string? Description { get; set; }
}

public enum Color { Red, Green, Blue }

/// <summary>自引用类型，用于验证 schema 深度上限。</summary>
public class Node
{
    public string Name { get; set; } = "";
    public Node? Child { get; set; }
}

/// <summary>
/// 承载复杂参数的工具，供生成器/运行时测试。partial 供源生成器生成 handler。
/// 不标 [ServiceRegister]，测试里手动 AddScoped 注册。
/// </summary>
public partial class ComplexParamsTestTools
{
    public ChoiceOption? LastOption { get; private set; }
    public List<ChoiceOption>? LastList { get; private set; }
    public ChoiceOption[]? LastArray { get; private set; }
    public Color LastColor { get; private set; }

    /// <summary>选一个选项。</summary>
    /// <param name="option">单个选项对象</param>
    /// <returns>所选 label</returns>
    [AiTool]
    public string PickOne(ChoiceOption option)
    {
        LastOption = option;
        return option.Label;
    }

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

    /// <summary>设置颜色。</summary>
    /// <param name="color">颜色枚举</param>
    /// <returns>所选颜色名</returns>
    [AiTool]
    public string SetColor(Color color)
    {
        LastColor = color;
        return color.ToString();
    }

    /// <summary>遍历节点。</summary>
    /// <param name="root">根节点（自引用）</param>
    /// <returns>根节点名</returns>
    [AiTool]
    public string Walk(Node root) => root.Name;

    /// <summary>可能为空的选项。</summary>
    /// <param name="option">可选选项</param>
    /// <returns>label 或 none</returns>
    [AiTool]
    public string Maybe(ChoiceOption? option) => option is null ? "none" : option.Label;
}
