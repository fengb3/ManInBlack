using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class RetryMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_NoError_ShouldPassthrough()
    {
        var middleware = new RetryMiddleware(NullLogger<RetryMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "test" };

        var update = new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("hello")]);
        var results = await middleware.HandleAsync(ctx,
            () => TestHelpers.AsyncSeq(update), CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal("hello", results[0].Text);
    }

    [Fact]
    public async Task HandleAsync_IOExceptionBeforeYield_ShouldRetry()
    {
        var middleware = new RetryMiddleware(NullLogger<RetryMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "test" };

        var callCount = 0;
        ChatResponseUpdateHandler next = () =>
        {
            callCount++;
            if (callCount == 1)
            {
                return TestHelpers.ThrowOnMoveNext<ChatResponseUpdate>(
                    new IOException("network error"));
            }
            // 第二次成功
            return TestHelpers.AsyncSeq(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("recovered")]));
        };

        var results = await middleware.HandleAsync(ctx, next, CancellationToken.None).ToListAsync();

        Assert.Equal(2, callCount);
        // 应包含重试通知和成功响应
        Assert.Contains(results.ExtractTexts(), t => t.Contains("retry"));
        Assert.Contains(results.ExtractTexts(), t => t.Contains("recovered"));
    }

    [Fact]
    public async Task HandleAsync_HttpRequestExceptionBeforeYield_ShouldRetry()
    {
        var middleware = new RetryMiddleware(NullLogger<RetryMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "test" };

        var callCount = 0;
        ChatResponseUpdateHandler next = () =>
        {
            callCount++;
            if (callCount == 1)
            {
                return TestHelpers.ThrowOnMoveNext<ChatResponseUpdate>(
                    new HttpRequestException("connection lost"));
            }
            return TestHelpers.AsyncSeq(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]));
        };

        var results = await middleware.HandleAsync(ctx, next, CancellationToken.None).ToListAsync();

        Assert.Equal(2, callCount);
        Assert.Contains(results.ExtractTexts(), t => t.Contains("ok"));
    }

    [Fact]
    public async Task HandleAsync_ThrowsAfterMaxRetriesExhausted()
    {
        var middleware = new RetryMiddleware(NullLogger<RetryMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "test" };

        var callCount = 0;
        ChatResponseUpdateHandler alwaysFail = () =>
        {
            callCount++;
            return TestHelpers.ThrowOnMoveNext<ChatResponseUpdate>(
                new IOException("persistent error"));
        };

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in middleware.HandleAsync(ctx, alwaysFail, CancellationToken.None)) { }
        });

        // 总共调用了 4 次：初始 + 3 次重试
        Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task HandleAsync_ExceptionWithNonRetryableType_ShouldThrowImmediately()
    {
        var middleware = new RetryMiddleware(NullLogger<RetryMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "test" };

        // InvalidOperationException 不在重试范围内，应直接抛
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in middleware.HandleAsync(ctx, () =>
                TestHelpers.ThrowOnMoveNext<ChatResponseUpdate>(new InvalidOperationException("bad state")),
                CancellationToken.None)) { }
        });
    }
}
