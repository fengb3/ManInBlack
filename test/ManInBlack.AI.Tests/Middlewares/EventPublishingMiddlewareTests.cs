using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Events;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Services;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class EventPublishingMiddlewareTests
{
    private static (EventBus bus, List<ModelContentEvent> events, IDisposable sub) CreateSubscription(string key)
    {
        var bus = new EventBus();
        var events = new List<ModelContentEvent>();
        var sub = bus.Subscribe<ModelContentEvent>(key, (e, _) => { events.Add(e); return Task.CompletedTask; });
        return (bus, events, sub);
    }

    [Fact]
    public async Task HandleAsync_TextContent_ShouldPublishAndYield()
    {
        var (bus, events, sub) = CreateSubscription("agent-1");
        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-1" };

        var update = new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("hello")]);
        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(update)).ToListAsync();

        // update 原样转发
        Assert.Single(results);
        Assert.Equal("hello", results[0].Text);

        // 事件：1 个 Text + 1 个 Completed
        Assert.Equal(2, events.Count);
        Assert.Equal(ModelContentKind.Text, events[0].Kind);
        Assert.Equal("hello", events[0].Text);
        Assert.Equal("agent-1", events[0].AgentId);
        Assert.Equal(ModelContentKind.Completed, events[1].Kind);

        sub.Dispose();
    }

    [Fact]
    public async Task HandleAsync_EmptyStream_ShouldOnlyPublishCompleted()
    {
        var (bus, events, sub) = CreateSubscription("agent-2");
        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-2" };

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Empty(results);
        Assert.Single(events);
        Assert.Equal(ModelContentKind.Completed, events[0].Kind);
        Assert.Equal("agent-2", events[0].AgentId);

        sub.Dispose();
    }

    [Fact]
    public async Task HandleAsync_ReasoningContent_ShouldPublishReasoningEvent()
    {
        var (bus, events, sub) = CreateSubscription("agent-3");
        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-3" };

        var update = new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("thinking...")]);
        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(update)).ToListAsync();

        Assert.Single(results);
        Assert.Equal(2, events.Count);
        Assert.Equal(ModelContentKind.Reasoning, events[0].Kind);
        Assert.Equal("thinking...", events[0].Text);
        Assert.Equal(ModelContentKind.Completed, events[1].Kind);

        sub.Dispose();
    }

    [Fact]
    public async Task HandleAsync_UsageContent_ShouldPublishUsageEvent()
    {
        var (bus, events, sub) = CreateSubscription("agent-4");
        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-4" };

        var usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20 };
        var update = new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(usage)]);
        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(update)).ToListAsync();

        Assert.Single(results);
        Assert.Equal(2, events.Count);
        Assert.Equal(ModelContentKind.Usage, events[0].Kind);
        Assert.NotNull(events[0].Usage);
        Assert.Equal(10, events[0].Usage!.InputTokenCount);
        Assert.Equal(20, events[0].Usage!.OutputTokenCount);
        Assert.Equal(ModelContentKind.Completed, events[1].Kind);

        sub.Dispose();
    }

    [Fact]
    public async Task HandleAsync_UnknownContent_ShouldNotPublishEvent()
    {
        var (bus, events, sub) = CreateSubscription("agent-5");
        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-5" };

        // FunctionCallContent 不在 switch 匹配中
        var update = new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "MyTool")]);
        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(update)).ToListAsync();

        // update 仍被转发
        Assert.Single(results);
        // 只有 Completed，没有 content 事件
        Assert.Single(events);
        Assert.Equal(ModelContentKind.Completed, events[0].Kind);

        sub.Dispose();
    }

    [Fact]
    public async Task HandleAsync_MultipleContentInOneUpdate_ShouldPublishEachEvent()
    {
        var (bus, events, sub) = CreateSubscription("agent-6");
        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-6" };

        var usage = new UsageDetails { InputTokenCount = 5 };
        var update = new ChatResponseUpdate(ChatRole.Assistant,
            [new TextContent("hi"), new UsageContent(usage)]);
        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(update)).ToListAsync();

        Assert.Single(results);
        // 1 个 Text + 1 个 Usage + 1 个 Completed
        Assert.Equal(3, events.Count);
        Assert.Equal(ModelContentKind.Text, events[0].Kind);
        Assert.Equal("hi", events[0].Text);
        Assert.Equal(ModelContentKind.Usage, events[1].Kind);
        Assert.Equal(ModelContentKind.Completed, events[2].Kind);

        sub.Dispose();
    }

    [Fact]
    public async Task HandleAsync_MultipleUpdates_ShouldPublishEventForEach()
    {
        var (bus, events, sub) = CreateSubscription("agent-7");
        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-7" };

        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("b")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("c")]),
        };

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(updates)).ToListAsync();

        Assert.Equal(3, results.Count);
        // 3 个 Text + 1 个 Completed
        Assert.Equal(4, events.Count);
        Assert.Equal(ModelContentKind.Text, events[0].Kind);
        Assert.Equal("a", events[0].Text);
        Assert.Equal(ModelContentKind.Text, events[1].Kind);
        Assert.Equal("b", events[1].Text);
        Assert.Equal(ModelContentKind.Text, events[2].Kind);
        Assert.Equal("c", events[2].Text);
        Assert.Equal(ModelContentKind.Completed, events[3].Kind);
        Assert.Equal("agent-7", events[3].AgentId);

        sub.Dispose();
    }

    [Fact]
    public async Task HandleAsync_CompletedShouldBeLastEvent()
    {
        var (bus, events, sub) = CreateSubscription("agent-8");
        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-8" };

        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("x")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("y")]),
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(updates)).ToListAsync();

        Assert.Equal(3, events.Count);
        Assert.Equal(ModelContentKind.Completed, events[^1].Kind);
        Assert.True(events[..^1].All(e => e.Kind != ModelContentKind.Completed));

        sub.Dispose();
    }
}
