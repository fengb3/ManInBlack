using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Commands;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Commands;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Commands;

public class BuiltinCommandsTests
{
    [Fact]
    public async Task New_ResetsSession_ClearsMessages_YieldsConfirmation()
    {
        var userStorage = new FakeUserStorage();
        var services = new ServiceCollection()
            .AddSingleton<IUserStorage>(userStorage)
            .BuildServiceProvider();
        var ctx = new AgentContext(services)
        {
            SessionId = "old-session",
            ParentId = "u1",
            Messages = [new(ChatRole.User, "/new"), new(ChatRole.Assistant, "old reply")],
        };
        var cmd = new BuiltinCommands();

        var results = await cmd.New(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.NotEqual("old-session", ctx.SessionId);          // 换了新 SessionId
        Assert.Empty(ctx.Messages);                              // 清空了
        Assert.Contains("已重置", results.Single().Text);
    }

    [Fact]
    public async Task Help_ListsRegisteredCommands()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new", Description = "重置对话" },
            new FakeHandler { CommandName = "help", Description = "帮助" },
        });
        var ctx = new AgentContext(new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider())
        {
            AgentId = "a1",
            UserInput = "/help",
            Messages = [],
        };
        var cmd = new BuiltinCommands();

        var results = await cmd.Help(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Contains("new", results.Single().Text);
        Assert.Contains("help", results.Single().Text);
    }
}

file sealed class FakeHandler : ICommandHandler
{
    public string CommandName { get; init; } = "";
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = "";
    public IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext c, ChatResponseUpdateHandler n, CancellationToken ct)
        => AsyncEnumerable.Empty<ChatResponseUpdate>();
}
