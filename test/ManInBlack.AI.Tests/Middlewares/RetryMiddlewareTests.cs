using System;
using System.IO;
using System.Net;
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

    [Fact]
    public async Task HandleAsync_HttpRequestException400_ShouldNotRetry()
    {
        var middleware = new RetryMiddleware(NullLogger<RetryMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "test" };

        var callCount = 0;
        ChatResponseUpdateHandler next = () =>
        {
            callCount++;
            return TestHelpers.ThrowOnMoveNext<ChatResponseUpdate>(
                new HttpRequestException("400 Bad Request", null, HttpStatusCode.BadRequest));
        };

        // 4xx 是确定性错误，应立即抛出、不重试
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in middleware.HandleAsync(ctx, next, CancellationToken.None)) { }
        });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task HandleAsync_HttpRequestException500_ShouldRetry()
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
                    new HttpRequestException("500", null, HttpStatusCode.InternalServerError));
            }
            return TestHelpers.AsyncSeq(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]));
        };

        var results = await middleware.HandleAsync(ctx, next, CancellationToken.None).ToListAsync();

        Assert.Equal(2, callCount);
        Assert.Contains(results.ExtractTexts(), t => t.Contains("ok"));
    }

    [Fact]
    public async Task HandleAsync_HttpRequestException429_ShouldRetry()
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
                    new HttpRequestException("429", null, HttpStatusCode.TooManyRequests));
            }
            return TestHelpers.AsyncSeq(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]));
        };

        var results = await middleware.HandleAsync(ctx, next, CancellationToken.None).ToListAsync();

        Assert.Equal(2, callCount);
        Assert.Contains(results.ExtractTexts(), t => t.Contains("ok"));
    }

    [Fact]
    public async Task HandleAsync_ExceptionAfterYield_ShouldNotRetryAndThrow()
    {
        var middleware = new RetryMiddleware(NullLogger<RetryMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "test" };

        var callCount = 0;
        ChatResponseUpdateHandler next = () =>
        {
            callCount++;
            return YieldThenThrow(new IOException("stream truncated"));
        };

        var ex = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in middleware.HandleAsync(ctx, next, CancellationToken.None)) { }
        });

        // 已输出过内容，不能整体重试；应直接抛原始异常
        Assert.Equal(1, callCount);
        Assert.Equal("stream truncated", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_ExceptionAfterYield_PartialContentIsStillObserved()
    {
        var middleware = new RetryMiddleware(NullLogger<RetryMiddleware>.Instance);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "test" };

        ChatResponseUpdateHandler next = () => YieldThenThrow(new IOException("stream truncated"));

        var observed = new List<string>();
        try
        {
            await foreach (var update in middleware.HandleAsync(ctx, next, CancellationToken.None))
            {
                if (update.Text is not null)
                    observed.Add(update.Text);
            }
        }
        catch (IOException)
        {
            // 预期会抛异常
        }

        Assert.Contains("partial", observed);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> YieldThenThrow(
        Exception ex,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial")]);
        throw ex;
    }
}
