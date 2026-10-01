using ManInBlack.AI.Abstraction;

namespace ManInBlack.AI;

/// <summary>
/// <see cref="AgentDefinition"/> 的类型化管道扩展。
/// </summary>
public static class AgentDefinitionExtensions
{
    /// <summary>
    /// 将管道设置为指定类型化管道，并同步 <see cref="AgentDefinition.PipelineName"/>。
    /// </summary>
    public static AgentDefinition SetPipeline<TPipeline>(this AgentDefinition definition) where TPipeline : IAgentPipeline
    {
        definition.PipelineType = typeof(TPipeline);
        definition.PipelineName = PipelineNameResolver.Resolve<TPipeline>();
        return definition;
    }
}
