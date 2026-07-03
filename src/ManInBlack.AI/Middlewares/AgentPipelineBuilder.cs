using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 聊天客户端中间件管道构建器，将多个 <see cref="AgentMiddleware"/> 和 <see cref="IChatClient"/> 组合为可调用的代理
/// </summary>
public class AgentPipelineBuilder
{
    private readonly List<Func<IServiceProvider, AgentMiddleware>> _middlewareFactories = [];
    // 中间件类型名（注册顺序 = 外→内 = 运行时调用顺序），用于在 Build 时合并为一行日志
    private readonly List<string> _middlewareNames = [];

    /// <summary>
    /// 添加中间件实例
    /// </summary>
    public AgentPipelineBuilder Use(AgentMiddleware middleware)
    {
        _middlewareFactories.Add(_ => middleware);
        _middlewareNames.Add(middleware.GetType().Name);
        return this;
    }

    /// <summary>
    /// 从依赖注入容器解析并添加中间件
    /// </summary>
    public AgentPipelineBuilder Use<TMiddleware>() where TMiddleware : AgentMiddleware
    {
        _middlewareFactories.Add(sp => sp.GetRequiredService<TMiddleware>());
        _middlewareNames.Add(typeof(TMiddleware).Name);
        return this;
    }

    /// <summary>
    /// 构建管道，返回可直接调用的代理函数
    /// </summary>
    public Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> Build(IServiceProvider serviceProvider)
    {
        // 将整条管道的中间件名合并为一行日志（外→内，即运行时调用顺序）
        serviceProvider
            .GetService<ILogger<AgentPipelineBuilder>>()
            ?.LogInformation("Resolving middleware pipeline: {Pipeline}", string.Join(" → ", _middlewareNames));

        var chatClient = serviceProvider.GetRequiredService<IChatClient>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        // 按 ModelChoice 缓存 IChatClient，避免每次流式调用都创建新客户端
        IChatClient? perAgentClient = null;

        Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> pipeline =
            context =>
            {
                if (context.Items.TryGetValue("ModelChoice", out var mc) && mc is ModelChoice choice)
                {
                    perAgentClient ??= ChatClientProviderExtensions.CreateChatClient(httpClientFactory, choice);
                    return perAgentClient.GetStreamingResponseAsync(context.Messages, context.Options);
                }
                return chatClient.GetStreamingResponseAsync(context.Messages, context.Options);
            };


        // 反向包裹中间件
        for (var i = _middlewareFactories.Count - 1; i >= 0; i--)
        {
            var middlewareFactory = _middlewareFactories[i];
            var next = pipeline;

            pipeline = context =>
            {
                var middle = middlewareFactory.Invoke(context.ServiceProvider);
                return middle.HandleAsync(context, () => next(context), context.CancellationToken);
            };
        }

        return pipeline;
    }
}