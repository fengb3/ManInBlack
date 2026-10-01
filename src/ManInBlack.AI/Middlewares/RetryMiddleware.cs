using System.Net;
using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
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
            var fatalError = "";

            var enumerator = next().GetAsyncEnumerator(ct);
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (Exception ex)
                {
                    // 非可重试异常（逻辑错误等）立即抛，不重试
                    if (!yielded && !IsRetryable(ex))
                        throw;

                    if (!yielded && attempt < MaxRetries)
                    {
                        shouldRetry = true;
                        break;
                    }

                    // 流式响应一旦开始输出，整体重试会导致内容重复/截断，语义不可接受：
                    // 立即释放枚举器并抛出原始异常，让上游观测到真实错误。
                    if (yielded)
                    {
                        await enumerator.DisposeAsync();
                        LogRetryAbortedAfterYield(logger, context.AgentId, attempt + 1, ex);
                        throw;
                    }

                    LogRetryExhausted(logger, context.AgentId, attempt + 1);
                    fatalError = ex.Message;
                    break;
                }

                if (!moved)
                    break;

                yielded = true;
                yield return enumerator.Current;
            }

            await enumerator.DisposeAsync();

            if (fatalError.Length > 0)
            {
                yield return new ChatResponseUpdate
                {
                    Contents =
                    [
                        new TextContent(
                            $"API 请求失败，已无法重试。错误：{fatalError}"
                        )
                    ]
                };
                throw new IOException(fatalError);
            }

            if (!shouldRetry)
                yield break;

            var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
            LogRetrying(logger, context.AgentId, attempt + 1, delay);
            yield return new ChatResponseUpdate
            {
                Contents =
                [
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

    [LoggerMessage(LogLevel.Error, "Agent {agentId} 流式响应已开始输出，第 {attempt} 次尝试异常后不再重试，直接抛出原始异常")]
    static partial void LogRetryAbortedAfterYield(ILogger<RetryMiddleware> logger, string agentId, int attempt, Exception exception);

    /// <summary>
    /// 判断异常是否值得重试。仅重试瞬时错误（连接级、超时、5xx、408、429）；
    /// 4xx 客户端错误（如 400 历史非法）是确定性的，重试无意义，立即抛。
    /// </summary>
    private static bool IsRetryable(Exception ex)
    {
        if (ex is not HttpRequestException hre)
            return ex is IOException or System.Net.Sockets.SocketException or TimeoutException;

        var status = hre.StatusCode;
        if (status is null) return true;                               // 连接级错误（无状态码），保守重试
        if (status == HttpStatusCode.RequestTimeout) return true;      // 408
        if (status == HttpStatusCode.TooManyRequests) return true;     // 429
        if (status >= HttpStatusCode.InternalServerError) return true; // 5xx 服务端错误
        return false;                                                  // 其余 4xx：确定性错误，不重试
    }
}
