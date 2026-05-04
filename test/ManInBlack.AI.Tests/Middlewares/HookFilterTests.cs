using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Tests.Helpers;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class HookFilterTests
{
    [Fact]
    public async Task ExecuteAsync_BeforeHook_ShouldFireWithToolContext()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor();
        var filter = new HookFilter(fakeExecutor, NullLogger<HookFilter>.Instance);
        var ctx = new ToolExecuteContext(TestHelpers.EmptyServiceProvider)
        {
            ToolName = "GetWeather",
            CallId = "call-123",
            Arguments = new Dictionary<string, object?> { { "city", "北京" } },
        };

        Task Next(ToolExecuteContext c) => Task.CompletedTask;

        // Act
        await filter.ExecuteAsync(ctx, Next);

        // Assert: BeforeToolExecute 钩子被触发，携带工具信息
        var beforeHook = fakeExecutor.ExecutedHooks
            .Find(h => h.Point == HookPoint.BeforeToolExecute);
        Assert.NotNull(beforeHook.Context);
        Assert.Equal("GetWeather", beforeHook.Context.ToolName);
        Assert.Equal("call-123", beforeHook.Context.CallId);
        Assert.NotNull(beforeHook.Context.ArgumentsJson);
        Assert.Contains("city", beforeHook.Context.ArgumentsJson);
    }

    [Fact]
    public async Task ExecuteAsync_BeforeHook_Blocked_ShouldNotCallNext()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor
        {
            Result = new HookResult { IsBlocked = true, BlockReason = "禁止访问" }
        };
        var filter = new HookFilter(fakeExecutor, NullLogger<HookFilter>.Instance);
        var ctx = new ToolExecuteContext(TestHelpers.EmptyServiceProvider)
        {
            ToolName = "DangerousTool",
            CallId = "call-blocked",
        };

        var nextCalled = false;
        Task Next(ToolExecuteContext c)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        // Act
        await filter.ExecuteAsync(ctx, Next);

        // Assert: next 不应被调用
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task ExecuteAsync_BeforeHook_Blocked_ShouldSetErrorMessage()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor
        {
            Result = new HookResult { IsBlocked = true, BlockReason = "权限不足" }
        };
        var filter = new HookFilter(fakeExecutor, NullLogger<HookFilter>.Instance);
        var ctx = new ToolExecuteContext(TestHelpers.EmptyServiceProvider)
        {
            ToolName = "RestrictedTool",
            CallId = "call-err",
        };

        Task Next(ToolExecuteContext c) => Task.CompletedTask;

        // Act
        await filter.ExecuteAsync(ctx, Next);

        // Assert: Error 被设置，Message 包含 BlockReason
        Assert.NotNull(ctx.Error);
        Assert.Contains("权限不足", ctx.Error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_BeforeHook_NotBlocked_ShouldCallNext()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor
        {
            Result = new HookResult { IsBlocked = false }
        };
        var filter = new HookFilter(fakeExecutor, NullLogger<HookFilter>.Instance);
        var ctx = new ToolExecuteContext(TestHelpers.EmptyServiceProvider)
        {
            ToolName = "SafeTool",
            CallId = "call-safe",
        };

        var nextCalled = false;
        Task Next(ToolExecuteContext c)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        // Act
        await filter.ExecuteAsync(ctx, Next);

        // Assert: next 应被调用
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ExecuteAsync_AfterHook_ShouldFireWithResult()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor();
        var filter = new HookFilter(fakeExecutor, NullLogger<HookFilter>.Instance);
        var ctx = new ToolExecuteContext(TestHelpers.EmptyServiceProvider)
        {
            ToolName = "QueryData",
            CallId = "call-result",
            Arguments = new Dictionary<string, object?> { { "q", "test" } },
        };

        Task Next(ToolExecuteContext c)
        {
            c.Result = "42 条记录";
            return Task.CompletedTask;
        }

        // Act
        await filter.ExecuteAsync(ctx, Next);

        // Assert: AfterToolExecute 钩子被触发，携带 Result 和 Error
        var afterHook = fakeExecutor.ExecutedHooks
            .Find(h => h.Point == HookPoint.AfterToolExecute);
        Assert.NotNull(afterHook.Context);
        Assert.Equal("QueryData", afterHook.Context.ToolName);
        Assert.Equal("42 条记录", afterHook.Context.ResultJson);
        Assert.Null(afterHook.Context.Error);
    }

    [Fact]
    public async Task ExecuteAsync_AfterHook_ShouldFireEvenOnError()
    {
        // Arrange
        var fakeExecutor = new FakeHookExecutor();
        var filter = new HookFilter(fakeExecutor, NullLogger<HookFilter>.Instance);
        var ctx = new ToolExecuteContext(TestHelpers.EmptyServiceProvider)
        {
            ToolName = "FailingTool",
            CallId = "call-fail",
        };

        Task Next(ToolExecuteContext c)
        {
            c.Error = new Exception("工具执行失败");
            return Task.CompletedTask;
        }

        // Act
        await filter.ExecuteAsync(ctx, Next);

        // Assert: 即使出错，AfterToolExecute 钩子仍然触发
        var afterHook = fakeExecutor.ExecutedHooks
            .Find(h => h.Point == HookPoint.AfterToolExecute);
        Assert.NotNull(afterHook.Context);
        Assert.Equal("FailingTool", afterHook.Context.ToolName);
        Assert.Equal("工具执行失败", afterHook.Context.Error);
    }
}
