using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class LoggingMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_ShouldPassthroughResponses()
    {
        var middleware = new LoggingMiddleware(NullLogger<LoggingMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "test-agent",
            UserInput = "hello",
            Messages = []
        };

        var update = new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("response")]);
        var results = await middleware.HandleAsync(ctx,
            () => TestHelpers.AsyncSeq(update)).ToListAsync();

        Assert.Single(results);
        Assert.Equal("response", results[0].Text);
    }

    [Fact]
    public async Task HandleAsync_EmptyStream_ShouldNotThrow()
    {
        var middleware = new LoggingMiddleware(NullLogger<LoggingMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "test",
            UserInput = "",
            Messages = []
        };

        // 不抛异常即通过
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();
    }
}
