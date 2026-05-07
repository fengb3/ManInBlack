using ManInBlack.AI.Abstraction.Middleware;

namespace ManInBlack.AI.Abstraction.Agent;

/// <summary>
/// Agent 工厂接口，负责根据定义创建并运行 Agent
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// 根据名称查找已注册的 Agent 定义并运行
    /// </summary>
    Task<AgentResult> RunAsync(string agentName, string input, AgentContext parentContext, CancellationToken ct);

    /// <summary>
    /// 根据指定的 Agent 定义运行
    /// </summary>
    Task<AgentResult> RunAsync(AgentDefinition definition, string input, AgentContext parentContext, CancellationToken ct);
}
