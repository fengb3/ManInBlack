using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Middlewares;

namespace ManInBlack.AI;

public static class AgentPipelineBuilderExtensions
{
    public static AgentPipelineBuilder UseDefault(this AgentPipelineBuilder builder)
    {
        return builder
                .Use<LoggingMiddleware>()
                .Use<MessageEnrichMiddleware>()
                .Use<SkillMiddleware>()
                .Use<AgentProfileMiddleware>()
                .Use<SystemPromptInjectionMiddleware>()
                .Use<ReadPersistenceMiddleware>()
                .Use<SavePersistenceMiddleware>()
                .Use<UserInputMiddleware>()
                .Use<ContextCompressMiddleware>()
                .Use<CommandToolMiddleware>()
                .Use<FileToolMiddleware>()
                .Use<AgentLoopMiddleware>() // Agent Loop 应该在最后一个
            ;
    }
}