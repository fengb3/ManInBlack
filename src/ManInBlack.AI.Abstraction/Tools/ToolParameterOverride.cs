namespace ManInBlack.AI.Abstraction.Tools;

/// <summary>
/// 工具参数覆盖配置类，用于动态修改工具参数的描述和类型信息
/// </summary>
public class ToolParameterOverride
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 参数的 JSON Schema 类型，默认为 "string"
    /// </summary>
    public string Type { get; set; } = "string";

    /// <summary>
    /// 参数的描述信息
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否为必填参数，默认为 false
    /// </summary>
    public bool Required { get; set; } = false;

    /// <summary>
    /// 参数是否可为空，默认为 false
    /// </summary>
    public bool IsNullable { get; set; } = false;
}