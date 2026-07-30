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
