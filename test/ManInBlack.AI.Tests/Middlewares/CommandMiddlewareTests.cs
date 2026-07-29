using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Commands;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Commands;
using ManInBlack.AI.Events;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Services;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

file sealed class FakeCommandHandler : ICommandHandler
{
    public string CommandName { get; init; } = "new";
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = "";
    public Func<AgentContext, ChatResponseUpdateHandler, CancellationToken,
        IAsyncEnumerable<ChatResponseUpdate>>? Impl { get; set; }
    public IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext c, ChatResponseUpdateHandler n, CancellationToken ct)
        => Impl?.Invoke(c, n, ct) ?? AsyncEnumerable.Empty<ChatResponseUpdate>();
}

public class CommandMiddlewareTests
{
    private static AgentContext NewContext(string userInput, out EventBus bus, out FakeHookExecutor hooks)
    {
        bus = new EventBus();
        hooks = new FakeHookExecutor();
        var services = new ServiceCollection()
            .AddSingleton(bus)
            .BuildServiceProvider();
        return new AgentContext(services)
        {
            AgentId = "agent-1",
            UserInput = userInput,
            Messages = [new(ChatRole.User, userInput)],
        };
    }

    private static CommandMiddleware NewMiddleware(SlashCommandRegistry registry, EventBus bus, FakeHookExecutor hooks)
        => new(registry, bus, hooks, NullLogger<CommandMiddleware>.Instance);

    [Fact]
    public async Task KnownCommand_IsDispatched_AndShortCircuits()
    {
        var ctx = NewContext("/new", out var bus, out var hooks);
        ChatResponseUpdate[] nextStream = [new(ChatRole.Assistant, [new TextContent("SHOULD-NOT-APPEAR")])];
        var handler = new FakeCommandHandler
        {
            CommandName = "new",
            Impl = (_, _, _) => TestHelpers.AsyncSeq(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("已重置")])),
        };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        var results = await middleware.HandleAsync(ctx, () => nextStream.ToAsyncEnumerable()).ToListAsync();

        Assert.Equal("已重置", results.Single().Text);
        Assert.DoesNotContain(results, u => u.Text == "SHOULD-NOT-APPEAR"); // next 未被调用
    }

    [Fact]
    public async Task KnownCommand_InjectsArgsIntoItems()
    {
        var ctx = NewContext("/model sonnet-4", out var bus, out var hooks);
        string[]? captured = null;
        var handler = new FakeCommandHandler
        {
            CommandName = "model",
            Impl = (c, _, _) =>
            {
                captured = c.GetCommandArgs();
                return AsyncEnumerable.Empty<ChatResponseUpdate>();
            },
        };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Equal(new[] { "sonnet-4" }, captured);
    }

    [Fact]
    public async Task UnknownCommand_YieldsHint_AndShortCircuits()
    {
        var ctx = NewContext("/foobar", out var bus, out var hooks);
        var middleware = NewMiddleware(new SlashCommandRegistry([]), bus, hooks);

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("LLM")]))).ToListAsync();

        Assert.Contains("未知命令 /foobar", results.Single().Text);
    }

    [Fact]
    public async Task NonCommand_PassesThroughToNext()
    {
        var ctx = NewContext("hello world", out var bus, out var hooks);
        var middleware = NewMiddleware(new SlashCommandRegistry([]), bus, hooks);

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("LLM-REPLY")]))).ToListAsync();

        Assert.Equal("LLM-REPLY", results.Single().Text);
    }

    [Fact]
    public async Task AfterRun_PublishesCommandExecutedEvent()
    {
        var ctx = NewContext("/new", out var bus, out var hooks);
        CommandExecutedEvent? captured = null;
        bus.Subscribe<CommandExecutedEvent>("agent-1", (evt, _) => { captured = evt; return Task.CompletedTask; });
        var handler = new FakeCommandHandler { CommandName = "new" };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.NotNull(captured);
        Assert.Equal("new", captured!.CommandName);
        Assert.True(captured.Succeeded);
    }

    [Fact]
    public async Task AfterRun_CallsAfterCommandHook()
    {
        var ctx = NewContext("/new", out var bus, out var hooks);
        var handler = new FakeCommandHandler { CommandName = "new" };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var call = Assert.Single(hooks.ExecutedHooks);
        Assert.Equal(HookPoint.AfterCommand, call.Point);
        Assert.Equal("new", call.Context.CommandName);
        Assert.True(call.Context.Succeeded);
    }

    [Fact]
    public async Task HandlerThrows_PublishesFailedEvent_AndRethrows()
    {
        var ctx = NewContext("/new", out var bus, out var hooks);
        CommandExecutedEvent? captured = null;
        bus.Subscribe<CommandExecutedEvent>("agent-1", (evt, _) => { captured = evt; return Task.CompletedTask; });
        var handler = new FakeCommandHandler
        {
            CommandName = "new",
            Impl = (_, _, _) => TestHelpers.ThrowOnMoveNext<ChatResponseUpdate>(new InvalidOperationException("boom")),
        };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync());

        Assert.NotNull(captured);
        Assert.False(captured!.Succeeded);
        Assert.Single(hooks.ExecutedHooks); // finally 仍触发 AfterCommand
    }
}
