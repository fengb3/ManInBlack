using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Middleware;

/// <summary>
/// 聊天客户端中间件管道构建器，将多个 <see cref="AgentMiddleware"/> 和 <see cref="IChatClient"/> 组合为可调用的代理
/// </summary>
public class AgentPipeline(IChatClient chatClient, IServiceProvider serviceProvider)
{
    private readonly List<AgentMiddleware> _middlewares = [];

    /// <summary>
    /// 添加中间件实例
    /// </summary>
    public AgentPipeline Use(AgentMiddleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// 从依赖注入容器解析并添加中间件
    /// </summary>
    public AgentPipeline Use<TMiddleware>() where TMiddleware : AgentMiddleware
    {
        var middleware = ActivatorUtilities.GetServiceOrCreateInstance<TMiddleware>(serviceProvider);
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// 构建管道，返回可直接调用的代理函数
    /// </summary>
    public Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> Build()
    {
        // 终点：调用底层 IChatClient
        Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> pipeline = context =>
            chatClient.GetStreamingResponseAsync(context.Messages, context.Options);

        // 反向包裹中间件
        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next       = pipeline;
            pipeline = context => middleware.HandleAsync(context, () => next(context), context.CancellationToken);
        }

        return pipeline;
    }
}