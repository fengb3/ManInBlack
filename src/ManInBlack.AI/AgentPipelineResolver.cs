using ManInBlack.AI.Middlewares;

namespace ManInBlack.AI;

/// <summary>
/// 将 <see cref="IAgentPipeline"/> 实现类型的静态 <see cref="IAgentPipeline.Configure"/> 方法
/// 转换为可存储的管道配置委托。
/// </summary>
internal static class AgentPipelineResolver
{
    public static Func<AgentPipelineBuilder, AgentPipelineBuilder> For<T>() where T : IAgentPipeline
        => b => T.Configure(b);
}
