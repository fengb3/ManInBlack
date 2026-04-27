using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 聊天客户端中间件管道构建器，将多个 <see cref="AgentMiddleware"/> 和 <see cref="IChatClient"/> 组合为可调用的代理
/// </summary>
public class AgentPipelineBuilder
{
    private readonly List<Func<IServiceProvider, AgentMiddleware>> _middlewareFactories = [];

    /// <summary>
    /// 添加中间件实例
    /// </summary>
    public AgentPipelineBuilder Use(AgentMiddleware middleware)
    {
        // _middlewares.Add(middleware);
        _middlewareFactories.Add(_ => middleware);
        return this;
    }

    /// <summary>
    /// 从依赖注入容器解析并添加中间件
    /// </summary>
    public AgentPipelineBuilder Use<TMiddleware>() where TMiddleware : AgentMiddleware
    {
        _middlewareFactories.Add(sp =>
        {
            ILogger<TMiddleware> logger = sp.GetRequiredService<ILogger<TMiddleware>>();
            logger.LogInformation("Resolving middleware {Middleware} ", typeof(TMiddleware).Name);
            return sp.GetRequiredService<TMiddleware>();
        });
        return this;
    }

    /// <summary>
    /// 构建管道，返回可直接调用的代理函数
    /// </summary>
    public Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> Build(IServiceProvider serviceProvider)
    {
        // // 终点：调用底层 IChatClient
        // Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> pipeline = context =>
        //     _chatClient.GetStreamingResponseAsync(context.Messages, context.Options);

        var chatClient = serviceProvider.GetRequiredService<IChatClient>();

        // message 第0条为 system prompt，最后1条为 user input，中间为 assistant 和 tool 消息
        Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> pipeline =
            context => { return chatClient.GetStreamingResponseAsync(context.Messages, context.Options); };


        // 反向包裹中间件
        for (var i = _middlewareFactories.Count - 1; i >= 0; i--)
        {
            var middlewareFactory = _middlewareFactories[i];
            var next = pipeline;

            pipeline = context =>
            {
                // ILogger<AgentPipelineBuilder> logger = serviceProvider.GetRequiredService<ILogger<AgentPipelineBuilder>>();
                // logger.LogInformation("Executing middleware {Middleware} for agent {AgentId}", middlewareFactory.Method.DeclaringType?.Name, context.AgentId);
                var middle = middlewareFactory.Invoke(context.ServiceProvider);
                return middle.HandleAsync(context, () => next(context), context.CancellationToken);
            };
        }

        return pipeline;
    }
}