using ManInBlack.AI.Abstraction;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 AgentDefinition。
/// </summary>
public sealed class AgentBuilder
{
    private readonly AgentDefinition _definition = new();

    internal AgentBuilder(string name) => _definition.Name = name;

    /// <summary>设置 Agent 描述。</summary>
    public AgentBuilder Description(string description) { _definition.Description = description; return this; }

    /// <summary>设置 Agent 系统指令。</summary>
    public AgentBuilder Instruction(string instruction) { _definition.Instruction = instruction; return this; }

    /// <summary>设置关联的 Pipeline 名称。</summary>
    public AgentBuilder Pipeline(string pipelineName) { _definition.PipelineName = pipelineName; return this; }

    /// <summary>设置子 Agent 名称列表。</summary>
    public AgentBuilder SubAgents(params string[] subAgents) { _definition.SubAgents = [..subAgents]; return this; }

    /// <summary>设置关联的 ModelChoice 名称（可选）。</summary>
    public AgentBuilder ModelChoice(string? modelChoiceName) { _definition.ModelChoiceName = modelChoiceName; return this; }

    internal AgentDefinition Build() => _definition;
}
