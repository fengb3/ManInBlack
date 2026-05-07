namespace ManInBlack.AI.Abstraction.Agent;

/// <summary>
/// Agent 的模型配置选项，Abstraction 层独立于主项目的 ModelChoice
/// </summary>
public sealed class AgentModelOptions
{
    /// <summary>
    /// 提供商名称，对应 IModelProvider.ProviderName
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// API 密钥
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 提供商基础地址
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// 模型标识
    /// </summary>
    public string ModelId { get; set; } = string.Empty;
}

/// <summary>
/// Agent 定义，描述一个 Agent 的静态配置
/// </summary>
public sealed class AgentDefinition
{
    /// <summary>
    /// Agent 名称，在注册表中唯一标识
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Agent 描述，用于 LLM 决定是否委托给此 Agent
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 系统提示词，即 Agent 的行为指令
    /// </summary>
    public string Instructions { get; set; } = string.Empty;

    /// <summary>
    /// 管道名称，决定 Agent 使用哪组中间件。
    /// 内置值："Default"（全功能）, "Simple"（最小）, "Coder", "Shell", "Analyst"。
    /// 默认 "Simple"。
    /// </summary>
    public string PipelineName { get; set; } = "Simple";

    /// <summary>
    /// Agent 使用的模型配置，为 null 时使用默认模型
    /// </summary>
    public AgentModelOptions? Model { get; set; }
}
