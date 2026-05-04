using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class HookMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_BeforeLlmCall_ShouldFireHook()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor();
        var middleware = new HookMiddleware(fakeExecutor);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "agent-1",
            SystemPrompt = "你是助手",
            UserInput = "你好",
        };

        // Act
        _ = await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream, CancellationToken.None)
            .ToListAsync();

        // Assert: BeforeLlmCall 钩子被触发
        Assert.Contains(fakeExecutor.ExecutedHooks, h => h.Point == HookPoint.BeforeLlmCall);
        var beforeHook = fakeExecutor.ExecutedHooks.Single(h => h.Point == HookPoint.BeforeLlmCall);
        Assert.Equal("agent-1", beforeHook.Context.AgentId);
        Assert.Equal("你是助手", beforeHook.Context.SystemPrompt);
        Assert.Equal("你好", beforeHook.Context.UserInput);
    }

    [Fact]
    public async Task HandleAsync_BeforeLlmCall_InjectedText_ShouldAppendToSystemPrompt()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor
        {
            Result = new HookResult { Succeeded = true, InjectedText = "附加规则：请用中文回答" }
        };
        var middleware = new HookMiddleware(fakeExecutor);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "agent-2",
            SystemPrompt = "你是助手",
            UserInput = "hello",
        };

        // Act
        _ = await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream, CancellationToken.None)
            .ToListAsync();

        // Assert: InjectedText 被追加到 SystemPrompt
        Assert.Equal("你是助手\n\n附加规则：请用中文回答", ctx.SystemPrompt);
    }

    [Fact]
    public async Task HandleAsync_BeforeLlmCall_InjectedTextWithExplicitTarget_ShouldAppendToSystemPrompt()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor
        {
            Result = new HookResult
            {
                Succeeded = true,
                InjectedText = "额外上下文",
                InjectTarget = "SystemPrompt"
            }
        };
        var middleware = new HookMiddleware(fakeExecutor);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "agent-3",
            SystemPrompt = "基础提示词",
            UserInput = "test",
        };

        // Act
        _ = await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream, CancellationToken.None)
            .ToListAsync();

        // Assert: 指定了 InjectTarget=SystemPrompt，仍然追加
        Assert.Equal("基础提示词\n\n额外上下文", ctx.SystemPrompt);
    }

    [Fact]
    public async Task HandleAsync_NoFunctionCalls_ShouldFireAgentCompleted()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor();
        var middleware = new HookMiddleware(fakeExecutor);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "agent-4",
            SystemPrompt = "sys",
            UserInput = "hi",
        };

        // next 返回的流中没有 FunctionCallContent
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("hello")])
        };

        // Act
        _ = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(updates), CancellationToken.None)
            .ToListAsync();

        // Assert: AgentCompleted 钩子被触发
        Assert.Contains(fakeExecutor.ExecutedHooks, h => h.Point == HookPoint.AgentCompleted);
        var completedHook = fakeExecutor.ExecutedHooks.Single(h => h.Point == HookPoint.AgentCompleted);
        Assert.Equal("agent-4", completedHook.Context.AgentId);
    }

    [Fact]
    public async Task HandleAsync_WithFunctionCalls_ShouldNotFireAgentCompleted()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor();
        var middleware = new HookMiddleware(fakeExecutor);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "agent-5",
            SystemPrompt = "sys",
            UserInput = "call tool",
        };

        // next 返回的流中包含 FunctionCallContent
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "MyTool", null)
            ])
        };

        // Act
        _ = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(updates), CancellationToken.None)
            .ToListAsync();

        // Assert: AgentCompleted 钩子不应被触发
        Assert.DoesNotContain(fakeExecutor.ExecutedHooks, h => h.Point == HookPoint.AgentCompleted);
    }

    [Fact]
    public async Task HandleAsync_BeforeLlmCall_EmptyResult_ShouldNotModifySystemPrompt()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor
        {
            Result = new HookResult { Succeeded = true, InjectedText = "" }
        };
        var middleware = new HookMiddleware(fakeExecutor);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "agent-6",
            SystemPrompt = "原始提示词",
            UserInput = "test",
        };

        // Act
        _ = await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream, CancellationToken.None)
            .ToListAsync();

        // Assert: 空 InjectedText 不应修改 SystemPrompt
        Assert.Equal("原始提示词", ctx.SystemPrompt);
    }
}
