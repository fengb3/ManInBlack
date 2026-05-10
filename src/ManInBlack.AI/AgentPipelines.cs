using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;

namespace ManInBlack.AI;

public static class AgentPipelineBuilderExtensions
{
    /// <summary>
    /// 默认
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static AgentPipelineBuilder UseDefault(this AgentPipelineBuilder builder) =>
        builder
            .Use<EventPublishingMiddleware>() // 在最外层, 用于ui监听agent事件
            .Use<ReadPersistenceMiddleware>()
            .Use<SavePersistenceMiddleware>()
            .Use<SkillMiddleware>()
            .Use<DelegationMiddleware>()
            .Use<AgentProfileMiddleware>()
            .Use<ContextCompressMiddleware>()
            .Use<CommandLineToolsMiddleware>()
            .Use<FileToolsMiddleware>()
            .UseSimple();
    
    
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