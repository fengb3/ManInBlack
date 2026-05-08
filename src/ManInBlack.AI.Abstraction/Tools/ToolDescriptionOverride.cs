namespace ManInBlack.AI.Abstraction.Tools;

/// <summary>
/// 工具描述覆盖配置类，用于动态修改工具的描述和参数信息
/// </summary>
public class ToolDescriptionOverride
{
    /// <summary>
    /// 要覆盖的工具名称，精确匹配
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// 覆盖工具的描述信息，如果为 null 则不覆盖
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 参数描述覆盖字典，键为参数名，值为新的参数描述
    /// </summary>
    public Dictionary<string, string>? ParameterOverrides { get; set; }

    /// <summary>
    /// 覆盖返回值的描述信息，如果为 null 则不覆盖
    /// </summary>
    public string? ReturnsDescription { get; set; }

    /// <summary>
    /// 动态新增的参数列表
    /// </summary>
    public List<ToolParameterOverride>? AdditionalParameters { get; set; }
}