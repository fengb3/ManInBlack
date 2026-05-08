namespace ManInBlack.AI.Abstraction.Factory;

/// <summary>
/// Agent 预设定义，包含创建 Agent 所需的静态配置信息
/// </summary>
public class AgentPreset
{
    /// <summary>预设名称（必填）</summary>
    public required string Name { get; init; }

    /// <summary>预设描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>系统提示词（即 Instruction，发送给 LLM 的 system prompt）</summary>
    public required string Instruction { get; init; }

    /// <summary>管道名称，决定使用哪组中间件组合。默认为 "Default"</summary>
    public string PipelineName { get; init; } = AgentPipelineNames.Default;
}