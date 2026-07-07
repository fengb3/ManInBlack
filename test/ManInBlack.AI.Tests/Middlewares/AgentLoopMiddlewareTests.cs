using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Events;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Services;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class AgentLoopMiddlewareTests
{
    private static IServiceProvider BuildSp(EventBus? bus = null)
    {
        bus ??= new EventBus();
        return new ServiceCollection()
            .AddSingleton(bus)
            .BuildServiceProvider();
    }

    [Fact]
    public async Task HandleAsync_NoToolCall_ShouldPassthrough()
    {
        var executor = new FakeToolExecutor();
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = [new(ChatRole.User, "what's 2+2?")]
        };

        var expected = new ChatResponseUpdate(ChatRole.Assistant,
            [new TextContent("The answer is 4")]);
        var results = await middleware.HandleAsync(ctx,
            () => TestHelpers.AsyncSeq(expected)).ToListAsync();

        Assert.Single(results);
        Assert.Equal("The answer is 4", results[0].Text);
        Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task HandleAsync_WithToolCall_ShouldExecuteAndLoop()
    {
        var executor = new FakeToolExecutor { Result = "file contents" };
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = [new(ChatRole.User, "read file test.txt")]
        };

        var callCount = 0;
        ChatResponseUpdateHandler next = () =>
        {
            callCount++;
            if (callCount == 1)
            {
                return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("call_1", "ReadFile",
                        new Dictionary<string, object?> { ["path"] = "test.txt" })
                ]));
            }
            return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [new TextContent("done reading")]));
        };

        var results = await middleware.HandleAsync(ctx, next).ToListAsync();

        Assert.Equal(2, callCount);
        Assert.Equal(1, executor.ExecuteCount);
        Assert.Equal("ReadFile", executor.ExecutedContexts[0].ToolName);
        Assert.Equal("file contents", executor.ExecutedContexts[0].Result);

        Assert.Equal(4, ctx.Messages.Count);
        Assert.Contains(ctx.Messages, m => m.Role == ChatRole.Tool);

        var finalText = results.Last().Contents.OfType<TextContent>().LastOrDefault();
        Assert.Contains("done", finalText?.Text ?? "");
    }

    [Fact]
    public async Task HandleAsync_MultipleToolCalls_ShouldExecuteAll()
    {
        var executor = new FakeToolExecutor { Result = "result" };
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = [new(ChatRole.User, "do stuff")]
        };

        var callCount = 0;
        ChatResponseUpdateHandler next = () =>
        {
            callCount++;
            if (callCount == 1)
            {
                return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("c1", "ToolA", null),
                    new FunctionCallContent("c2", "ToolB", null)
                ]));
            }
            return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [new TextContent("all done")]));
        };

        var results = await middleware.HandleAsync(ctx, next).ToListAsync();

        Assert.Equal(2, executor.ExecuteCount);
        Assert.Equal("ToolA", executor.ExecutedContexts[0].ToolName);
        Assert.Equal("ToolB", executor.ExecutedContexts[1].ToolName);
    }

    [Fact]
    public async Task HandleAsync_ShouldAccumulateUsage()
    {
        var executor = new FakeToolExecutor();
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = []
        };

        var updateWithUsage = new ChatResponseUpdate(ChatRole.Assistant,
        [
            new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 })
        ]);

        await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(updateWithUsage)).ToListAsync();

        Assert.Equal(10, ctx.AccumulatedUsage.InputTokenCount);
        Assert.Equal(5, ctx.AccumulatedUsage.OutputTokenCount);
    }

    [Fact]
    public async Task HandleAsync_ShouldIncludeReasoningInAssistantMessage()
    {
        var executor = new FakeToolExecutor();
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = [new(ChatRole.User, "think through this")]
        };

        var update = new ChatResponseUpdate(ChatRole.Assistant,
        [
            new TextReasoningContent("let me think..."),
            new TextContent("my answer")
        ]);

        await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(update)).ToListAsync();

        var assistantMsg = ctx.Messages.Last(m => m.Role == ChatRole.Assistant);
        Assert.Contains(assistantMsg.Contents, c => c is TextReasoningContent);
        Assert.Contains(assistantMsg.Contents, c => c is TextContent);
    }

    [Fact]
    public async Task HandleAsync_ToolError_ShouldBeRecorded()
    {
        var executor = new FakeToolExecutor { Error = new InvalidOperationException("tool failed") };
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = []
        };

        var callCount = 0;
        ChatResponseUpdateHandler next = () =>
        {
            callCount++;
            if (callCount == 1)
            {
                return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("c1", "BrokenTool", null)
                ]));
            }
            return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [new TextContent("handled error")]));
        };

        var results = await middleware.HandleAsync(ctx, next).ToListAsync();

        var toolMsg = ctx.Messages.First(m => m.Role == ChatRole.Tool);
        var frc = toolMsg.Contents.OfType<FunctionResultContent>().First();
        Assert.Contains("tool failed", frc.Result?.ToString());
    }

    [Fact]
    public async Task HandleAsync_ShouldPublishAfterLlmCallAndAllToolsCompletedEvents()
    {
        var executor = new FakeToolExecutor { Result = "result" };
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = [new(ChatRole.User, "do stuff")]
        };

        var afterLlmCallCount = 0;
        var allToolsCompletedCount = 0;
        bus.Subscribe<AfterLlmCallEvent>(EventBus.HookKey("test-agent"), (evt, ct) =>
        {
            afterLlmCallCount++;
            return Task.CompletedTask;
        });
        bus.Subscribe<AllToolsCompletedEvent>(EventBus.HookKey("test-agent"), (evt, ct) =>
        {
            allToolsCompletedCount++;
            return Task.CompletedTask;
        });

        var callCount = 0;
        ChatResponseUpdateHandler next = () =>
        {
            callCount++;
            if (callCount == 1)
            {
                return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("c1", "ToolA", null)
                ]));
            }
            return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [new TextContent("done")]));
        };

        await middleware.HandleAsync(ctx, next).ToListAsync();

        // 2 次 LLM 调用 → 2 次 AfterLlmCall
        Assert.Equal(2, afterLlmCallCount);
        // 1 批工具执行 → 1 次 AllToolsCompleted
        Assert.Equal(1, allToolsCompletedCount);
    }
}
