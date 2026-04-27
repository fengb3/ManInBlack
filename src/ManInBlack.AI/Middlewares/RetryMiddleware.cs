using System.Runtime.CompilerServices;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 流式请求重试中间件，捕获 TLS/网络异常并自动重试
/// </summary>
[ServiceRegister.Scoped]
public partial class RetryMiddleware(ILogger<RetryMiddleware> logger) : AgentMiddleware
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)];

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var yielded = false;
            var shouldRetry = false;
            var exMessage = "";

            var enumerator = next().GetAsyncEnumerator(ct);
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException)
                {
                    exMessage = ex.Message;
                    if (!yielded && attempt < MaxRetries)
                    {
                        shouldRetry = true;
                        break;
                    }

                    LogRetryExhausted(logger, context.AgentId, attempt + 1);
                    await enumerator.DisposeAsync();
                    throw;
                }

                if (!moved)
                    break;

                yielded = true;
                yield return enumerator.Current;
            }

            await enumerator.DisposeAsync();

            if (!shouldRetry)
                yield break;

            var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
            LogRetrying(logger, context.AgentId, attempt + 1, delay);
            // let the outer ui display know what's happening
            yield return new ChatResponseUpdate()
            {
                Contents = [
                    new TextContent(
                        $"Error when calling api retry {attempt + 1} times in {delay.Seconds} second(s)"
                    )
                ]
            };
            await Task.Delay(delay, ct);

        }
    }

    [LoggerMessage(LogLevel.Warning, "Agent {agentId} 流式请求失败，第 {attempt} 次重试，等待 {delay}")]
    static partial void LogRetrying(ILogger<RetryMiddleware> logger, string agentId, int attempt, TimeSpan delay);

    [LoggerMessage(LogLevel.Error, "Agent {agentId} 流式请求重试 {attempt} 次后仍然失败")]
    static partial void LogRetryExhausted(ILogger<RetryMiddleware> logger, string agentId, int attempt);
}
