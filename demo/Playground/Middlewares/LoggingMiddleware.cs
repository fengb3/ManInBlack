using System.Runtime.CompilerServices;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Playground.Middlewares;

/// <summary>
/// 日志中间件示例，打印请求和响应信息
/// </summary>
public partial class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Log发送Count条消息模型Model(logger, context.Messages.Count, context.Options?.ModelId ?? "unknown");

        await foreach (var update in next().WithCancellation(ct))
            yield return update;

        Log响应完成(logger);
    }
    
    [LoggerMessage(LogLevel.Information, "→ 发送 {Count} 条消息，模型：{Model}")] static partial void Log发送Count条消息模型Model(ILogger<LoggingMiddleware> logger, int Count, string Model);
    [LoggerMessage(LogLevel.Information, "← 响应完成")] static partial void Log响应完成(ILogger<LoggingMiddleware> logger);
}