using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using ManInBlack.AI.Tests.Helpers;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class AgentLifecycleFilterTests
{
    private static (EventBus bus, FakeHookExecutor fake, IServiceProvider sp, List<IDisposable> subs) Setup(
        HookResult? beforeResult = null, HookResult? afterResult = null)
    {
        var bus = new EventBus();
        var fake = new FakeHookExecutor();
        var subs = new List<IDisposable>();

        subs.Add(bus.Subscribe<BeforeToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.BeforeToolExecute.ToString(),
                AgentId = evt.AgentId,
                ToolName = evt.ToolName,
                CallId = evt.CallId,
                ArgumentsJson = evt.ArgumentsJson,
            };
            fake.Result = beforeResult ?? new HookResult();
            var result = await fake.ExecuteAsync(HookPoint.BeforeToolExecute, hookCtx, ct);
            if (result.IsBlocked)
            {
                evt.IsBlocked = true;
                evt.BlockReason = result.BlockReason;
            }
        }));

        subs.Add(bus.Subscribe<AfterToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.AfterToolExecute.ToString(),
                AgentId = evt.AgentId,
                ToolName = evt.ToolName,
                CallId = evt.CallId,
                ArgumentsJson = evt.ArgumentsJson,
                ResultJson = evt.ResultJson,
                Error = evt.Error,
            };
            fake.Result = afterResult ?? new HookResult();
            await fake.ExecuteAsync(HookPoint.AfterToolExecute, hookCtx, ct);
        }));

        var agentCtx = new AgentContext(
            new ServiceCollection().AddSingleton<EventBus>(bus).BuildServiceProvider())
        {
            AgentId = "test-agent"
        };
        var sp = new ServiceCollection()
            .AddSingleton(agentCtx)
            .BuildServiceProvider();

        return (bus, fake, sp, subs);
    }

    [Fact]
    public async Task ExecuteAsync_BeforeHook_ShouldFireWithToolContext()
    {
        // Arrange
        var (_, fake, sp, subs) = Setup();
        var bus = new EventBus();
        var agentCtx = new AgentContext(
            new ServiceCollection().AddSingleton<EventBus>(bus).BuildServiceProvider())
        { AgentId = "test-agent" };
        var serviceProvider = new ServiceCollection()
            .AddSingleton<EventBus>(bus)
            .AddSingleton(agentCtx)
            .BuildServiceProvider();

        var filter = new AgentLifecycleFilter(bus, NullLogger<AgentLifecycleFilter>.Instance);
        var ctx = new ToolExecuteContext(serviceProvider)
        {
            ToolName = "GetWeather",
            CallId = "call-123",
            Arguments = new Dictionary<string, object?> { { "city", "北京" } },
        };

        Task Next(ToolExecuteContext c) => Task.CompletedTask;

        // Act
        await filter.ExecuteAsync(ctx, Next);

        // Assert
        var beforeHook = fake.ExecutedHooks.Find(h => h.Point == HookPoint.BeforeToolExecute);
        Assert.NotNull(beforeHook.Context);
        Assert.Equal("GetWeather", beforeHook.Context.ToolName);
        Assert.Equal("call-123", beforeHook.Context.CallId);
        Assert.NotNull(beforeHook.Context.ArgumentsJson);
        Assert.Contains("city", beforeHook.Context.ArgumentsJson);

        foreach (var sub in subs) sub.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_BeforeHook_Blocked_ShouldNotCallNext()
    {
        // Arrange
        var bus = new EventBus();
        var agentCtx = new AgentContext(
            new ServiceCollection().AddSingleton<EventBus>(bus).BuildServiceProvider())
        { AgentId = "test-agent" };
        var serviceProvider = new ServiceCollection()
            .AddSingleton<EventBus>(bus)
            .AddSingleton(agentCtx)
            .BuildServiceProvider();

        var fake = new FakeHookExecutor
        {
            Result = new HookResult { IsBlocked = true, BlockReason = "禁止访问" }
        };
        var sub = bus.Subscribe<BeforeToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.BeforeToolExecute.ToString(),
                ToolName = evt.ToolName,
                CallId = evt.CallId,
            };
            var result = await fake.ExecuteAsync(HookPoint.BeforeToolExecute, hookCtx, ct);
            if (result.IsBlocked)
            {
                evt.IsBlocked = true;
                evt.BlockReason = result.BlockReason;
            }
        });

        var filter = new AgentLifecycleFilter(bus, NullLogger<AgentLifecycleFilter>.Instance);
        var ctx = new ToolExecuteContext(serviceProvider)
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

        // Assert
        Assert.False(nextCalled);
        sub.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_BeforeHook_Blocked_ShouldSetErrorMessage()
    {
        // Arrange
        var bus = new EventBus();
        var agentCtx = new AgentContext(
            new ServiceCollection().AddSingleton<EventBus>(bus).BuildServiceProvider())
        { AgentId = "test-agent" };
        var serviceProvider = new ServiceCollection()
            .AddSingleton<EventBus>(bus)
            .AddSingleton(agentCtx)
            .BuildServiceProvider();

        var fake = new FakeHookExecutor
        {
            Result = new HookResult { IsBlocked = true, BlockReason = "权限不足" }
        };
        var sub = bus.Subscribe<BeforeToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.BeforeToolExecute.ToString(),
                ToolName = evt.ToolName,
                CallId = evt.CallId,
            };
            var result = await fake.ExecuteAsync(HookPoint.BeforeToolExecute, hookCtx, ct);
            if (result.IsBlocked)
            {
                evt.IsBlocked = true;
                evt.BlockReason = result.BlockReason;
            }
        });

        var filter = new AgentLifecycleFilter(bus, NullLogger<AgentLifecycleFilter>.Instance);
        var ctx = new ToolExecuteContext(serviceProvider)
        {
            ToolName = "RestrictedTool",
            CallId = "call-err",
        };

        Task Next(ToolExecuteContext c) => Task.CompletedTask;

        // Act
        await filter.ExecuteAsync(ctx, Next);

        // Assert
        Assert.NotNull(ctx.Error);
        Assert.Contains("权限不足", ctx.Error.Message);
        sub.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_BeforeHook_NotBlocked_ShouldCallNext()
    {
        // Arrange
        var bus = new EventBus();
        var agentCtx = new AgentContext(
            new ServiceCollection().AddSingleton<EventBus>(bus).BuildServiceProvider())
        { AgentId = "test-agent" };
        var serviceProvider = new ServiceCollection()
            .AddSingleton<EventBus>(bus)
            .AddSingleton(agentCtx)
            .BuildServiceProvider();

        var fake = new FakeHookExecutor
        {
            Result = new HookResult { IsBlocked = false }
        };
        var sub = bus.Subscribe<BeforeToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.BeforeToolExecute.ToString(),
                ToolName = evt.ToolName,
                CallId = evt.CallId,
            };
            await fake.ExecuteAsync(HookPoint.BeforeToolExecute, hookCtx, ct);
        });
        sub = bus.Subscribe<AfterToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.AfterToolExecute.ToString(),
                ToolName = evt.ToolName,
                CallId = evt.CallId,
            };
            await fake.ExecuteAsync(HookPoint.AfterToolExecute, hookCtx, ct);
        });

        var filter = new AgentLifecycleFilter(bus, NullLogger<AgentLifecycleFilter>.Instance);
        var ctx = new ToolExecuteContext(serviceProvider)
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

        // Assert
        Assert.True(nextCalled);
        sub.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_AfterHook_ShouldFireWithResult()
    {
        // Arrange
        var bus = new EventBus();
        var agentCtx = new AgentContext(
            new ServiceCollection().AddSingleton<EventBus>(bus).BuildServiceProvider())
        { AgentId = "test-agent" };
        var serviceProvider = new ServiceCollection()
            .AddSingleton<EventBus>(bus)
            .AddSingleton(agentCtx)
            .BuildServiceProvider();

        var fake = new FakeHookExecutor();
        var subs = new List<IDisposable>();
        subs.Add(bus.Subscribe<BeforeToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.BeforeToolExecute.ToString(),
                ToolName = evt.ToolName,
                CallId = evt.CallId,
                ArgumentsJson = evt.ArgumentsJson,
            };
            await fake.ExecuteAsync(HookPoint.BeforeToolExecute, hookCtx, ct);
        }));
        subs.Add(bus.Subscribe<AfterToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.AfterToolExecute.ToString(),
                ToolName = evt.ToolName,
                CallId = evt.CallId,
                ArgumentsJson = evt.ArgumentsJson,
                ResultJson = evt.ResultJson,
                Error = evt.Error,
            };
            await fake.ExecuteAsync(HookPoint.AfterToolExecute, hookCtx, ct);
        }));

        var filter = new AgentLifecycleFilter(bus, NullLogger<AgentLifecycleFilter>.Instance);
        var ctx = new ToolExecuteContext(serviceProvider)
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

        // Assert
        var afterHook = fake.ExecutedHooks.Find(h => h.Point == HookPoint.AfterToolExecute);
        Assert.NotNull(afterHook.Context);
        Assert.Equal("QueryData", afterHook.Context.ToolName);
        Assert.Equal("42 条记录", afterHook.Context.ResultJson);
        Assert.Null(afterHook.Context.Error);

        foreach (var sub in subs) sub.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_AfterHook_ShouldFireEvenOnError()
    {
        // Arrange
        var bus = new EventBus();
        var agentCtx = new AgentContext(
            new ServiceCollection().AddSingleton<EventBus>(bus).BuildServiceProvider())
        { AgentId = "test-agent" };
        var serviceProvider = new ServiceCollection()
            .AddSingleton<EventBus>(bus)
            .AddSingleton(agentCtx)
            .BuildServiceProvider();

        var fake = new FakeHookExecutor();
        var subs = new List<IDisposable>();
        subs.Add(bus.Subscribe<BeforeToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.BeforeToolExecute.ToString(),
                ToolName = evt.ToolName,
                CallId = evt.CallId,
            };
            await fake.ExecuteAsync(HookPoint.BeforeToolExecute, hookCtx, ct);
        }));
        subs.Add(bus.Subscribe<AfterToolExecuteEvent>("test-agent", async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.AfterToolExecute.ToString(),
                ToolName = evt.ToolName,
                CallId = evt.CallId,
                ResultJson = evt.ResultJson,
                Error = evt.Error,
            };
            await fake.ExecuteAsync(HookPoint.AfterToolExecute, hookCtx, ct);
        }));

        var filter = new AgentLifecycleFilter(bus, NullLogger<AgentLifecycleFilter>.Instance);
        var ctx = new ToolExecuteContext(serviceProvider)
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

        // Assert
        var afterHook = fake.ExecutedHooks.Find(h => h.Point == HookPoint.AfterToolExecute);
        Assert.NotNull(afterHook.Context);
        Assert.Equal("FailingTool", afterHook.Context.ToolName);
        Assert.Equal("工具执行失败", afterHook.Context.Error);

        foreach (var sub in subs) sub.Dispose();
    }
}
