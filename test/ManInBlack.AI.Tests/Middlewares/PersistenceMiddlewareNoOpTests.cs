using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class PersistenceMiddlewareNoOpTests
{
    [Fact]
    public async Task ReadPersistenceMiddleware_NoStorageRegistered_IsNoOp()
    {
        var services = new ServiceCollection();
        services.AddScoped<AgentContext>();
        var sp = services.BuildServiceProvider();

        var context = new AgentContext(sp);
        context.SessionId = "s1";
        context.AgentName = "test";
        context.Messages.Add(new ChatMessage(ChatRole.User, "hi"));

        var middleware = new ReadPersistenceMiddleware();
        var updates = middleware.HandleAsync(context, () =>
            new[] { new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("ok")] } }
                .ToAsyncEnumerable(), default);

        var list = new List<ChatResponseUpdate>();
        await foreach (var u in updates)
            list.Add(u);

        Assert.Single(list);
        Assert.Single(context.Messages); // 没有额外加载历史
    }

    [Fact]
    public async Task SavePersistenceMiddleware_NoStorageRegistered_IsNoOp()
    {
        var services = new ServiceCollection();
        services.AddScoped<AgentContext>();
        var sp = services.BuildServiceProvider();

        var context = new AgentContext(sp);
        context.SessionId = "s1";
        context.AgentName = "test";
        context.Messages.Add(new ChatMessage(ChatRole.User, "hi"));

        var middleware = new SavePersistenceMiddleware();
        var updates = middleware.HandleAsync(context, () =>
            new[] { new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("ok")] } }
                .ToAsyncEnumerable(), default);

        var list = new List<ChatResponseUpdate>();
        await foreach (var u in updates)
            list.Add(u);

        Assert.Single(list);
    }
}
