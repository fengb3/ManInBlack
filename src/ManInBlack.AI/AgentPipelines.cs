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
            .Use<ReadPersistenceMiddleware>()
            .Use<SavePersistenceMiddleware>()
            .Use<SkillMiddleware>()
            .Use<SubAgentMiddleware>()
            .Use<AgentProfileMiddleware>()
            .Use<ContextCompressMiddleware>()
            .Use<CommandLineToolsMiddleware>()
            .Use<FileToolsMiddleware>()
            .Use<SubAgentToolsMiddleware>()
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

    /// <summary>
    /// Coder Agent 管道：命令行工具 + 文件工具 + 基础管道
    /// </summary>
    public static AgentPipelineBuilder UseCoder(this AgentPipelineBuilder builder) =>
        builder
            .Use<CommandLineToolsMiddleware>()
            .Use<FileToolsMiddleware>()
            .UseSimple();

    /// <summary>
    /// Shell Agent 管道：命令行工具 + 基础管道
    /// </summary>
    public static AgentPipelineBuilder UseShell(this AgentPipelineBuilder builder) =>
        builder
            .Use<CommandLineToolsMiddleware>()
            .UseSimple();

    /// <summary>
    /// Analyst Agent 管道：文件工具 + 基础管道
    /// </summary>
    public static AgentPipelineBuilder UseAnalyst(this AgentPipelineBuilder builder) =>
        builder
            .Use<FileToolsMiddleware>()
            .UseSimple();
}