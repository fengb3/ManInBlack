using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Factory;

/// <summary>
/// Agent 工厂创建的结果，包含已配置的 AgentContext 和可执行的 Pipeline
/// </summary>
public class AgentInstance
{
    /// <summary>已配置的 Agent 上下文</summary>
    public required AgentContext Context { get; init; }

    /// <summary>可执行的管道函数</summary>
    public required Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> Pipeline { get; init; }
}