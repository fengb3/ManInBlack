using AgentConsole.Middlewares;
using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Middleware;

namespace ManInBlack.AI;

public static class AgentPipelineBuilderExtensions
{
    
    public static AgentPipelineBuilder UseDefault(this AgentPipelineBuilder builder)
    {
        return builder
            .Use<MessageEnrichMiddleware>()
            .Use<SystemPromptInjectionMiddleware>()
            .Use<ReadPersistenceMiddleware>()
            .Use<SavePersistenceMiddleware>()
            .Use<UserInputMiddleware>()
            .Use<ContextCompressMiddleware>()
            .Use<CommandToolMiddleware>()
            .Use<AgentLoopMiddleware>()// Agent Loop 应该在最后一个
        ;
    }
}