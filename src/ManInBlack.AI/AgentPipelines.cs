using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;

namespace ManInBlack.AI;

public static class AgentPipelineBuilderExtensions
{
    /// <summary>
    /// 默认管道（无自定义插入点）。等价于 <see cref="UseDefault(AgentPipelineBuilder, Func{AgentPipelineBuilder, AgentPipelineBuilder}?)"/> 传 null。
    /// </summary>
    public static AgentPipelineBuilder UseDefault(this AgentPipelineBuilder builder) =>
        builder.UseDefault(null);

    /// <summary>
    /// 默认管道，支持在 <see cref="ToolsMiddleware"/> 和 <see cref="UseSimple"/> 之间插入自定义中间件。
    /// 典型用法：<c>UseDefault(b =&gt; b.Use(new ToolIntentSchemaMiddleware()))</c>。
    /// </summary>
    /// <param name="beforeSimple">插入点：此时 ToolsMiddleware 已注册、UseSimple 尚未调用。</param>
    public static AgentPipelineBuilder UseDefault(
        this AgentPipelineBuilder builder,
        Func<AgentPipelineBuilder, AgentPipelineBuilder>? beforeSimple)
    {
        builder
            .Use<EventPublishingMiddleware>() // 在最外层, 用于ui监听agent事件
            .Use<ReadPersistenceMiddleware>()
            .Use<SavePersistenceMiddleware>()
            .Use<SkillMiddleware>()
            .Use<DelegationMiddleware>()
            .Use<AgentProfileMiddleware>()
            .Use<ContextCompressMiddleware>()
            .Use<ToolsMiddleware>();

        if (beforeSimple is not null)
            builder = beforeSimple(builder);

        return builder.UseSimple();
    }
    
    
    /// <summary>
    /// 最小
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static AgentPipelineBuilder UseSimple(this AgentPipelineBuilder builder) =>
        builder
            .Use<LoggingMiddleware>()
            .Use<MessageEnrichMiddleware>()
            .Use<HookMiddleware>()
            .Use<SystemPromptInjectionMiddleware>()
            .Use<UserInputMiddleware>()
            .Use<RetryMiddleware>()
            .Use<AgentLoopMiddleware>(); // Agent Loop 应该在最后一个
}
