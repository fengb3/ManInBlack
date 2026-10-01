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
    /// 类型化管道引用（可选）。设置后 <see cref="PipelineName"/> 会同步为对应字符串名称，
    /// 最终仍落为字符串以兼容 JSON 配置。
    /// </summary>
    public Type? PipelineType { get; set; }

    /// <summary>
    /// 父 Agent 名称（可选）
    /// </summary>
    public string? ParentAgentName { get; set; }

    /// <summary>
    /// 可委托的子 Agent 名称列表。如果非空，DelegationMiddleware 会注入委托工具和提示词。
    /// </summary>
    public List<string> SubAgents { get; set; } = [];

    /// <summary>
    /// 引用的 ModelChoice 名称（可选）。不填则使用全局默认 ModelChoice。
    /// </summary>
    public string? ModelChoiceName { get; set; }
}
