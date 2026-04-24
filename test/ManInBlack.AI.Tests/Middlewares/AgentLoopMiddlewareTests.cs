using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Core.Tools;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class AgentLoopMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_NoToolCall_ShouldPassthrough()
    {
        var executor = new FakeToolExecutor();
        var logger = NullLogger<AgentContext>.Instance;
        var middleware = new AgentLoopMiddleware(executor, logger);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
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
        var logger = NullLogger<AgentContext>.Instance;
        var middleware = new AgentLoopMiddleware(executor, logger);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            Messages = [new(ChatRole.User, "read file test.txt")]
        };

        var callCount = 0;
        ChatResponseUpdateHandler next = () =>
        {
            callCount++;
            if (callCount == 1)
            {
                // 第一轮：返回 tool call
                return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("call_1", "ReadFile",
                        new Dictionary<string, object?> { ["path"] = "test.txt" })
                ]));
            }
            // 第二轮：最终文本
            return TestHelpers.AsyncSeq(new ChatResponseUpdate(ChatRole.Assistant,
                [new TextContent("done reading")]));
        };

        var results = await middleware.HandleAsync(ctx, next).ToListAsync();

        Assert.Equal(2, callCount);
        Assert.Equal(1, executor.ExecuteCount);
        Assert.Equal("ReadFile", executor.ExecutedContexts[0].ToolName);
        Assert.Equal("file contents", executor.ExecutedContexts[0].Result);

        // 消息历史应有 user → assistant(tool_call) → tool(result) → assistant(final)
        Assert.Equal(4, ctx.Messages.Count);
        Assert.Contains(ctx.Messages, m => m.Role == ChatRole.Tool);

        // 最终输出应包含 "done reading"
        var finalText = results.Last().Contents.OfType<TextContent>().LastOrDefault();
        Assert.Contains("done", finalText?.Text ?? "");
    }

    [Fact]
    public async Task HandleAsync_MultipleToolCalls_ShouldExecuteAll()
    {
        var executor = new FakeToolExecutor { Result = "result" };
        var logger = NullLogger<AgentContext>.Instance;
        var middleware = new AgentLoopMiddleware(executor, logger);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
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
        var logger = NullLogger<AgentContext>.Instance;
        var middleware = new AgentLoopMiddleware(executor, logger);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { Messages = [] };

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
        var logger = NullLogger<AgentContext>.Instance;
        var middleware = new AgentLoopMiddleware(executor, logger);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
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
        var logger = NullLogger<AgentContext>.Instance;
        var middleware = new AgentLoopMiddleware(executor, logger);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider) { Messages = [] };

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

        // 错误消息应作为 tool result 返回
        var toolMsg = ctx.Messages.First(m => m.Role == ChatRole.Tool);
        var frc = toolMsg.Contents.OfType<FunctionResultContent>().First();
        Assert.Contains("tool failed", frc.Result?.ToString());
    }
}
