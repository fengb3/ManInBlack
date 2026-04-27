using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class SystemPromptInjectionMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_ShouldInsertSystemMessageAtHead()
    {
        var middleware = new SystemPromptInjectionMiddleware(NullLogger<SystemPromptInjectionMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            SystemPrompt = "You are a helpful assistant.",
            Messages = []
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Single(ctx.Messages);
        Assert.Equal(ChatRole.System, ctx.Messages[0].Role);
        Assert.Equal("You are a helpful assistant.", ctx.Messages[0].Text);
    }

    [Fact]
    public async Task HandleAsync_ShouldInsertBeforeExistingMessages()
    {
        var middleware = new SystemPromptInjectionMiddleware(NullLogger<SystemPromptInjectionMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            SystemPrompt = "System",
            Messages = [new(ChatRole.User, "hello"), new(ChatRole.Assistant, "world")]
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Equal(3, ctx.Messages.Count);
        Assert.Equal(ChatRole.System, ctx.Messages[0].Role);
        Assert.Equal(ChatRole.User, ctx.Messages[1].Role);
        Assert.Equal(ChatRole.Assistant, ctx.Messages[2].Role);
    }

    [Fact]
    public async Task HandleAsync_ShouldPassthroughResponses()
    {
        var middleware = new SystemPromptInjectionMiddleware(NullLogger<SystemPromptInjectionMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            SystemPrompt = "test",
            Messages = []
        };

        var expected = new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("response")]);
        var results = await middleware.HandleAsync(ctx,
            () => TestHelpers.AsyncSeq(expected)).ToListAsync();

        Assert.Single(results);
        Assert.Equal("response", results[0].Text);
    }
}

public class UserInputMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_ShouldAddUserMessage()
    {
        var middleware = new UserInputMiddleware();
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            UserInput = "Hello AI",
            Messages = [new(ChatRole.System, "system prompt")]
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Equal(2, ctx.Messages.Count);
        Assert.Equal(ChatRole.User, ctx.Messages[1].Role);
        Assert.Equal("Hello AI", ctx.Messages[1].Text);
    }

    [Fact]
    public async Task HandleAsync_EmptyInput_StillAddsMessage()
    {
        var middleware = new UserInputMiddleware();
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            UserInput = "",
            Messages = []
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Single(ctx.Messages);
        Assert.Equal(ChatRole.User, ctx.Messages[0].Role);
        Assert.Equal("", ctx.Messages[0].Text);
    }
}
