using ManInBlack.AI.Middlewares;

namespace ManInBlack.AI.Pipelines;

/// <summary>
/// 内置默认管道（产品形态）。等价于 <see cref="AgentPipelineBuilderExtensions.UseDefault(AgentPipelineBuilder)"/>。
/// </summary>
[PipelineName("default")]
public sealed class DefaultPipeline : IAgentPipeline
{
    public static AgentPipelineBuilder Configure(AgentPipelineBuilder builder) => builder.UseDefault();
}
