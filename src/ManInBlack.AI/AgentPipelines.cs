using AgentConsole.Middlewares;
using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Middlewares;
using AgentLoopMiddleware=ManInBlack.AI.Middlewares.AgentLoopMiddleware;
using ContextCompressMiddleware=ManInBlack.AI.Middlewares.ContextCompressMiddleware;
using MessageEnrichMiddleware=ManInBlack.AI.Middlewares.MessageEnrichMiddleware;
using ReadPersistenceMiddleware=ManInBlack.AI.Middlewares.ReadPersistenceMiddleware;
using SavePersistenceMiddleware=ManInBlack.AI.Middlewares.SavePersistenceMiddleware;
using SkillMiddleware=ManInBlack.AI.Middlewares.SkillMiddleware;

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
                .Use<CommandLineToolsMiddleware>()
                .Use<FileToolsMiddleware>()
                .Use<AgentLoopMiddleware>() // Agent Loop 应该在最后一个
            ;
    }
}