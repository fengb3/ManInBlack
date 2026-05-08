namespace ManInBlack.AI.Factory;

using ManInBlack.AI.Abstraction.Factory;

/// <summary>
/// Agent 预设的流式构建器，用于在 DI 注册时配置 Agent 预设
/// </summary>
public class AgentPresetBuilder
{
    private string? _name;
    private string _description = string.Empty;
    private string? _instruction;
    private string _pipelineName = AgentPipelineNames.Default;

    /// <summary>
    /// 设置预设名称（必填）
    /// </summary>
    public AgentPresetBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    /// 设置预设描述
    /// </summary>
    public AgentPresetBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>
    /// 设置系统提示词（必填）
    /// </summary>
    public AgentPresetBuilder WithInstruction(string instruction)
    {
        _instruction = instruction;
        return this;
    }

    /// <summary>
    /// 设置管道名称，决定使用哪组中间件组合
    /// </summary>
    public AgentPresetBuilder UsePipeline(string pipelineName)
    {
        _pipelineName = pipelineName;
        return this;
    }

    /// <summary>
    /// 构建 AgentPreset 实例
    /// </summary>
    /// <exception cref="InvalidOperationException">当必填字段未设置时抛出</exception>
    public AgentPreset Build()
    {
        if (string.IsNullOrWhiteSpace(_name))
            throw new InvalidOperationException("预设名称不能为空，请先调用 WithName()");
        if (string.IsNullOrWhiteSpace(_instruction))
            throw new InvalidOperationException("系统提示词不能为空，请先调用 WithInstruction()");

        return new AgentPreset
        {
            Name = _name,
            Description = _description,
            Instruction = _instruction,
            PipelineName = _pipelineName
        };
    }
}