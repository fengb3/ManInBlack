using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Core.Middleware;

public delegate IAsyncEnumerable<ChatResponseUpdate> ChatResponseUpdateHandler();

/// <summary>
/// 聊天客户端中间件抽象基类，通过 <see cref="HandleAsync"/> 拦截请求和响应
/// </summary>
public abstract class AgentMiddleware
{
    /// <summary>
    /// 处理聊天请求，调用 <paramref name="next"/> 将请求传递给下一个中间件并获取响应更新流
    /// </summary>
    public abstract IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        CancellationToken ct = default
    );
}