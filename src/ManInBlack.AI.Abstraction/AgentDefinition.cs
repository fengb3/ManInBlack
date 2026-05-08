namespace ManInBlack.AI.Abstraction;

/// <summary>
/// Agent 定义，描述一个 Agent 的配置信息
/// </summary>
public class AgentDefinition
{
    /// <summary>
    /// Agent 名称，唯一标识
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Agent 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string Instruction { get; set; } = string.Empty;

    /// <summary>
    /// 管道名称
    /// </summary>
    public string PipelineName { get; set; } = "default";

    /// <summary>
    /// 父 Agent 名称（可选）
    /// </summary>
    public string? ParentAgentName { get; set; }
}
