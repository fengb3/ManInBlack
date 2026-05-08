namespace ManInBlack.AI.Configuration;

/// <summary>
/// 工具描述配置
/// </summary>
public class ToolDescriptionSetting
{
    /// <summary>工具名称</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>工具描述，用于覆盖默认描述</summary>
    public string? Description { get; set; }

    /// <summary>参数描述覆盖字典，参数名 → 新描述</summary>
    public Dictionary<string, string>? ParameterOverrides { get; set; }

    /// <summary>返回值描述覆盖</summary>
    public string? ReturnsDescription { get; set; }

    /// <summary>动态新增参数列表</summary>
    public List<ToolParameterSetting>? AdditionalParameters { get; set; }
}

/// <summary>
/// 工具参数配置
/// </summary>
public class ToolParameterSetting
{
    /// <summary>参数名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>参数类型，默认为 "string"</summary>
    public string Type { get; set; } = "string";

    /// <summary>参数描述</summary>
    public string? Description { get; set; }

    /// <summary>是否为必需参数，默认为 false</summary>
    public bool Required { get; set; } = false;

    /// <summary>是否允许为空，默认为 false</summary>
    public bool IsNullable { get; set; } = false;
}