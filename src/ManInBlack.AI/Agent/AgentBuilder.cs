using ManInBlack.AI.Abstraction.Agent;

namespace ManInBlack.AI.Agent;

/// <summary>
/// Agent 构建器，提供 Fluent API 用于配置并创建 AgentDefinition
/// </summary>
public sealed class AgentBuilder
{
    private readonly string _name;
    private string _description = string.Empty;
    private string _instructions = string.Empty;
    private string _pipelineName = "Simple";
    private AgentModelOptions? _model;

    /// <summary>
    /// 创建指定名称的 Agent 构建器
    /// </summary>
    public AgentBuilder(string name)
    {
        _name = name;
    }

    /// <summary>
    /// 设置 Agent 描述，用于 LLM 决定是否委托给此 Agent
    /// </summary>
    public AgentBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>
    /// 设置系统提示词，即 Agent 的行为指令
    /// </summary>
    public AgentBuilder WithInstructions(string instructions)
    {
        _instructions = instructions;
        return this;
    }

    /// <summary>
    /// 设置管道名称
    /// </summary>
    public AgentBuilder WithPipeline(string pipelineName)
    {
        _pipelineName = pipelineName;
        return this;
    }

    /// <summary>
    /// 设置 Agent 使用的模型配置，为 null 时使用默认模型
    /// </summary>
    public AgentBuilder WithModel(AgentModelOptions model)
    {
        _model = model;
        return this;
    }

    /// <summary>
    /// 构建并返回 AgentDefinition 实例
    /// </summary>
    public AgentDefinition Build()
    {
        return new AgentDefinition
        {
            Name = _name,
            Description = _description,
            Instructions = _instructions,
            PipelineName = _pipelineName,
            Model = _model,
        };
    }
}
