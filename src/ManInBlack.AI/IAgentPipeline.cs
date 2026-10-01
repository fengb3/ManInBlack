using ManInBlack.AI.Middlewares;

namespace ManInBlack.AI;

/// <summary>
/// 类型化管道约定。实现类通过 <see cref="Configure"/> 静态抽象方法描述中间件组合，
/// 供 <see cref="ManInBlackBuilderExtensions.AddPipeline{TPipeline}(IManInBlackBuilder)"/> 注册。
/// </summary>
public interface IAgentPipeline
{
    /// <summary>
    /// 配置中间件管道。
    /// </summary>
    /// <param name="builder">管道构建器。</param>
    /// <returns>配置后的管道构建器。</returns>
    static abstract AgentPipelineBuilder Configure(AgentPipelineBuilder builder);
}
