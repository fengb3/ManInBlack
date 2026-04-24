using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class MessageEnrichMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_ShouldSetCreatedAtOnNewMessage()
    {
        var middleware = new MessageEnrichMiddleware();
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            Messages = [new(ChatRole.User, "hello")]
        };

        // next 内部添加一条消息，包装集合应该给补上 CreatedAt
        ChatResponseUpdateHandler next = () =>
        {
            ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, "world"));
            return TestHelpers.EmptyStream;
        };

        await middleware.HandleAsync(ctx, next).ToListAsync();

        var last = ctx.Messages.Last();
        Assert.NotNull(last.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotOverwriteExistingCreatedAt()
    {
        var middleware = new MessageEnrichMiddleware();
        var existingTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            Messages = [new(ChatRole.User, "hello") { CreatedAt = existingTime }]
        };

        ChatResponseUpdateHandler next = () =>
        {
            // 已有时戳的消息应该保持不变
            ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, "world")
            {
                CreatedAt = existingTime
            });
            return TestHelpers.EmptyStream;
        };

        await middleware.HandleAsync(ctx, next).ToListAsync();

        Assert.Equal(existingTime, ctx.Messages[1].CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldRestoreOriginalMessages()
    {
        var middleware = new MessageEnrichMiddleware();
        var original = new List<ChatMessage> { new(ChatRole.User, "hi") };
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { Messages = original };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 中间件结束后应恢复原始列表引用（含新消息）
        Assert.Same(original, ctx.Messages);
    }
}

public class ContextCompressMiddlewareTests
{
    private static ChatMessage MakeToolResult(string callId, string result)
    {
        return new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent(callId, new TextContent(result))]);
    }

    [Fact]
    public async Task HandleAsync_ShouldKeepLast10FunctionResults()
    {
        var middleware = new ContextCompressMiddleware();
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 12; i++)
            messages.Add(MakeToolResult($"call_{i}", $"this is a long result that exceeds 36 characters to trigger compression for item {i}"));

        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { Messages = messages };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 内存顺序：最早在前。最后 10 个（索引 2-11）应保留，前 2 个应被压缩
        var allResults = ctx.Messages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();

        Assert.Equal(12, allResults.Count);
        // 最早的被压缩
        Assert.Equal("[old tool result content cleared]",
            allResults[0].Result?.ToString());
        Assert.Equal("[old tool result content cleared]",
            allResults[1].Result?.ToString());
        // 最新的 10 个保持不变
        Assert.Equal("this is a long result that exceeds 36 characters to trigger compression for item 2",
            allResults[2].Result?.ToString());
        Assert.Equal("this is a long result that exceeds 36 characters to trigger compression for item 11",
            allResults[11].Result?.ToString());
    }

    [Fact]
    public async Task HandleAsync_ShortResultNotCompressed()
    {
        var middleware = new ContextCompressMiddleware();
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 12; i++)
            messages.Add(MakeToolResult($"call_{i}", "ab")); // 比 placeholder 短

        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { Messages = messages };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 短结果不压缩，全保持原样
        var allResults = ctx.Messages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();
        Assert.All(allResults, r => Assert.Equal("ab", r.Result?.ToString()));
    }

    [Fact]
    public async Task HandleAsync_NoFunctionResults_DoesNothing()
    {
        var middleware = new ContextCompressMiddleware();
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            Messages = [new(ChatRole.User, "hi"), new(ChatRole.Assistant, "hello")]
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Equal(2, ctx.Messages.Count);
    }

    [Fact]
    public async Task HandleAsync_ShouldPassthroughResponses()
    {
        var middleware = new ContextCompressMiddleware();
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { Messages = [] };

        var expected = new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
        var results = await middleware.HandleAsync(ctx,
            () => TestHelpers.AsyncSeq(expected)).ToListAsync();

        Assert.Single(results);
        Assert.Equal("ok", results[0].Text);
    }
}
