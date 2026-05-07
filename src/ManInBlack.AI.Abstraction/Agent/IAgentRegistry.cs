namespace ManInBlack.AI.Abstraction.Agent;

/// <summary>
/// Agent 注册表接口，管理 Agent 定义的注册与查找
/// </summary>
public interface IAgentRegistry
{
    /// <summary>
    /// 注册一个 Agent 定义
    /// </summary>
    void Register(AgentDefinition definition);

    /// <summary>
    /// 根据名称获取 Agent 定义，未找到时返回 null
    /// </summary>
    AgentDefinition? Get(string name);

    /// <summary>
    /// 获取所有已注册的 Agent 定义
    /// </summary>
    IReadOnlyList<AgentDefinition> GetAll();
}
