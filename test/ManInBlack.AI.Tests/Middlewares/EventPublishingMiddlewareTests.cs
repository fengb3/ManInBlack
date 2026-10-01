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

    [Fact]
    public async Task HandleAsync_ArgumentDelta_ShouldPublishToolCallStreamEvent()
    {
        var bus = new EventBus();
        var events = new List<ToolCallStreamEvent>();
        using var sub = bus.Subscribe<ToolCallStreamEvent>("agent-delta", (e, _) =>
        {
            events.Add(e);
            return Task.CompletedTask;
        });

        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-delta" };

        var update = new ChatResponseUpdate(ChatRole.Assistant,
            [CreateDeltaFunctionCall("call-1", "ReadFile", "{\"path\":\"/etc/hosts\"}")]);

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(update)).ToListAsync();

        Assert.Single(results);
        Assert.Single(events);
        Assert.Equal("agent-delta", events[0].AgentId);
        Assert.Equal("call-1", events[0].CallId);
        Assert.Equal("ReadFile", events[0].ToolName);
        Assert.Equal("{\"path\":\"/etc/hosts\"}", events[0].AccumulatedArguments);
        Assert.Equal("{\"path\":\"/etc/hosts\"}", events[0].ArgumentDelta);
    }

    [Fact]
    public async Task HandleAsync_ArgumentDeltaInMultipleUpdates_ShouldAccumulateAndDiff()
    {
        var bus = new EventBus();
        var events = new List<ToolCallStreamEvent>();
        using var sub = bus.Subscribe<ToolCallStreamEvent>("agent-delta2", (e, _) =>
        {
            events.Add(e);
            return Task.CompletedTask;
        });

        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-delta2" };

        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant,
                [CreateDeltaFunctionCall("call-1", "ReadFile", "{\"path\":\"")]),
            new ChatResponseUpdate(ChatRole.Assistant,
                [CreateDeltaFunctionCall("call-1", "ReadFile", "{\"path\":\"/etc/hosts\"")]),
            new ChatResponseUpdate(ChatRole.Assistant,
                [CreateDeltaFunctionCall("call-1", "ReadFile", "{\"path\":\"/etc/hosts\",\"enco")]),
            new ChatResponseUpdate(ChatRole.Assistant,
                [CreateDeltaFunctionCall("call-1", "ReadFile", "{\"path\":\"/etc/hosts\",\"encoding\":\"utf-8\"}")]),
        };

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(updates)).ToListAsync();

        Assert.Equal(4, results.Count);
        Assert.Equal(4, events.Count);
        Assert.Equal("{\"path\":\"", events[0].ArgumentDelta);
        Assert.Equal("/etc/hosts\"", events[1].ArgumentDelta);
        Assert.Equal(",\"enco", events[2].ArgumentDelta);
        Assert.Equal("ding\":\"utf-8\"}", events[3].ArgumentDelta);
        Assert.Equal("{\"path\":\"/etc/hosts\",\"encoding\":\"utf-8\"}", events[3].AccumulatedArguments);
    }

    [Fact]
    public async Task HandleAsync_MultipleCallIds_ShouldTrackIndependently()
    {
        var bus = new EventBus();
        var events = new List<ToolCallStreamEvent>();
        using var sub = bus.Subscribe<ToolCallStreamEvent>("agent-multi", (e, _) =>
        {
            events.Add(e);
            return Task.CompletedTask;
        });

        var middleware = new EventPublishingMiddleware(bus);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { AgentId = "agent-multi" };

        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant,
                [CreateDeltaFunctionCall("c1", "ToolA", "{\"a\":1")]),
            new ChatResponseUpdate(ChatRole.Assistant,
                [CreateDeltaFunctionCall("c2", "ToolB", "{\"b\":2")]),
            new ChatResponseUpdate(ChatRole.Assistant,
                [CreateDeltaFunctionCall("c1", "ToolA", "{\"a\":12")]),
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(updates)).ToListAsync();

        Assert.Equal(3, events.Count);
        Assert.Equal("{\"a\":1", events[0].AccumulatedArguments);
        Assert.Equal("{\"b\":2", events[1].AccumulatedArguments);
        Assert.Equal("2", events[2].ArgumentDelta);
        Assert.Equal("{\"a\":12", events[2].AccumulatedArguments);
    }

    private static FunctionCallContent CreateDeltaFunctionCall(string callId, string name, string accumulatedJson)
    {
        var fcc = new FunctionCallContent(callId, name)
        {
            InformationalOnly = true,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ToolCallStreamEventKeys.IsArgumentDelta] = true,
                [ToolCallStreamEventKeys.AccumulatedArgumentsJson] = accumulatedJson
            }
        };
        return fcc;
    }
}
