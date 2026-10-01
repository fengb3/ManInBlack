using ManInBlack.AI.Middlewares;

namespace ManInBlack.AI.Pipelines;

/// <summary>
/// 内置最小管道。等价于 <see cref="AgentPipelineBuilderExtensions.UseSimple(AgentPipelineBuilder)"/>。
/// 库消费者推荐从该管道起步。
/// </summary>
[PipelineName("simple")]
public sealed class SimplePipeline : IAgentPipeline
{
    public static AgentPipelineBuilder Configure(AgentPipelineBuilder builder) => builder.UseSimple();
}
