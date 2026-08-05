using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

    /// <summary>
    /// 构造一个“开始执行即被打断”的执行器：拿到 semaphore 后取消令牌并抛 OCE，
    /// 模拟工具执行期间用户打断。FakeToolExecutor 自身不观测 ct、不吞异常，OCE 会从
    /// AgentLoopMiddleware 的 Task.WhenAll 抛出。
    /// </summary>
    private static FakeToolExecutor InterruptingExecutor(CancellationTokenSource cts, string result = "x") =>
        new()
        {
            Result = result,
            OnExecute = _ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }
        };

    [Fact]
    public async Task HandleAsync_CancelledDuringToolExecution_AppendsToolResultsForEveryCallId()
    {
        var cts = new CancellationTokenSource();
        var executor = InterruptingExecutor(cts);
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = []
        };

        ChatResponseUpdateHandler next = () => TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "SlowTool", null)]));

        // 取消被妥善处理，不应抛 OperationCanceledException
        await middleware.HandleAsync(ctx, next, cts.Token).ToListAsync();

        // 历史保持一致：assistant(tool_calls) 后必须存在覆盖全部 CallId 的 tool 结果
        var assistantMsg = ctx.Messages.Single(m =>
            m.Role == ChatRole.Assistant && m.Contents.OfType<FunctionCallContent>().Any());
        var expectedCallIds = assistantMsg.Contents.OfType<FunctionCallContent>()
            .Select(f => f.CallId).ToArray();

        var toolMsg = ctx.Messages.Single(m => m.Role == ChatRole.Tool);
        var actualCallIds = toolMsg.Contents.OfType<FunctionResultContent>()
            .Select(r => r.CallId).ToArray();

        Assert.Equal(expectedCallIds, actualCallIds);
        // 结果是中断桩
        Assert.All(toolMsg.Contents.OfType<FunctionResultContent>(),
            r => Assert.Contains("中断", r.Result?.ToString() ?? ""));
    }

    [Fact]
    public async Task HandleAsync_CancelledDuringMultipleToolExecution_AllCallIdsCovered()
    {
        var cts = new CancellationTokenSource();
        var executor = InterruptingExecutor(cts);
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = []
        };

        ChatResponseUpdateHandler next = () => TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
        [
            new FunctionCallContent("c1", "ToolA", null),
            new FunctionCallContent("c2", "ToolB", null)
        ]));

        await middleware.HandleAsync(ctx, next, cts.Token).ToListAsync();

        var toolMsg = ctx.Messages.Single(m => m.Role == ChatRole.Tool);
        var resultIds = toolMsg.Contents.OfType<FunctionResultContent>()
            .Select(r => r.CallId).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "c1", "c2" }, resultIds);
    }

    [Fact]
    public async Task HandleAsync_CancelledDuringToolExecution_DoesNotPublishAllToolsCompleted()
    {
        var cts = new CancellationTokenSource();
        var executor = InterruptingExecutor(cts);
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = []
        };

        var allToolsCompletedCount = 0;
        bus.Subscribe<AllToolsCompletedEvent>(EventBus.HookKey("test-agent"), (evt, ct) =>
        {
            allToolsCompletedCount++;
            return Task.CompletedTask;
        });

        ChatResponseUpdateHandler next = () => TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("c1", "ToolA", null)]));

        await middleware.HandleAsync(ctx, next, cts.Token).ToListAsync();

        Assert.Equal(0, allToolsCompletedCount);
    }

    [Fact]
    public async Task HandleAsync_CancelledDuringToolExecution_DoesNotRunSaveCheckpoint()
    {
        var cts = new CancellationTokenSource();
        var executor = InterruptingExecutor(cts);
        var middleware = new AgentLoopMiddleware(executor, NullLogger<AgentContext>.Instance);
        var bus = new EventBus();
        var ctx = new AgentContext(BuildSp(bus))
        {
            AgentId = "test-agent",
            Messages = []
        };

        var checkpointInvoked = 0;
        ctx.Items["SaveCheckpoint"] = (Func<string?, CancellationToken, Task>)((_, _) =>
        {
            checkpointInvoked++;
            return Task.CompletedTask;
        });

        ChatResponseUpdateHandler next = () => TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("c1", "ToolA", null)]));

        await middleware.HandleAsync(ctx, next, cts.Token).ToListAsync();

        Assert.Equal(0, checkpointInvoked);
    }
}
