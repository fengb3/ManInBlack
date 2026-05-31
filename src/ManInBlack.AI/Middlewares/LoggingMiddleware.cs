using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 打印日志
/// </summary>
[ServiceRegister.Scoped]
public partial class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        LogAgentAgentIdReceivedInputInput(logger, context.AgentId, context.UserInput);

        await foreach (var update in next().WithCancellation(ct)) yield return update;
        
        LogAgentAgentIdCompletedResponse(logger, context.AgentId);
    }

    [LoggerMessage(LogLevel.Information, "Agent {agentId} received input: {input}")]
    static partial void LogAgentAgentIdReceivedInputInput(ILogger<LoggingMiddleware> logger, string agentId, string input);

    [LoggerMessage(LogLevel.Information, "Agent {agentId} completed response")]
    static partial void LogAgentAgentIdCompletedResponse(ILogger<LoggingMiddleware> logger, string agentId);
}