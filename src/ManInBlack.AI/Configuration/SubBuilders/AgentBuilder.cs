using ManInBlack.AI.Abstraction;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 AgentDefinition。
/// </summary>
public sealed class AgentBuilder
{
    private readonly AgentDefinition _definition = new();

    internal AgentBuilder(string name) => _definition.Name = name;

    public AgentBuilder Description(string description) { _definition.Description = description; return this; }
    public AgentBuilder Instruction(string instruction) { _definition.Instruction = instruction; return this; }
    public AgentBuilder Pipeline(string pipelineName) { _definition.PipelineName = pipelineName; return this; }
    public AgentBuilder SubAgents(params string[] subAgents) { _definition.SubAgents = [..subAgents]; return this; }
    public AgentBuilder ModelChoice(string? modelChoiceName) { _definition.ModelChoiceName = modelChoiceName; return this; }

    internal AgentDefinition Build() => _definition;
}
