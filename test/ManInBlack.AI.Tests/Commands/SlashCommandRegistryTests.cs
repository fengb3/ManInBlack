using ManInBlack.AI.Abstraction.Commands;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Commands;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Commands;

// 用于测试的假 handler:不需要源生成器即可验证注册表逻辑
file sealed class FakeHandler : ICommandHandler
{
    public string CommandName { get; init; } = "";
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = "";
    public IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext context, ChatResponseUpdateHandler next, CancellationToken ct)
        => AsyncEnumerable.Empty<ChatResponseUpdate>();
}

public class SlashCommandRegistryTests
{
    [Fact]
    public void TryGet_FindsByCommandName()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new", Description = "重置对话" }
        });

        Assert.True(registry.TryGet("new", out var h));
        Assert.Equal("new", h!.CommandName);
    }

    [Fact]
    public void TryGet_FindsByAlias()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new", Aliases = ["clear", "reset"] }
        });

        Assert.True(registry.TryGet("clear", out _));
        Assert.True(registry.TryGet("reset", out _));
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new" }
        });

        Assert.True(registry.TryGet("NEW", out _));
        Assert.True(registry.TryGet("New", out _));
    }

    [Fact]
    public void TryGet_ReturnsFalseForUnknown()
        => Assert.False(new SlashCommandRegistry([]).TryGet("nope", out _));

    [Fact]
    public void Commands_DedupsAliases()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new", Aliases = ["clear", "reset"], Description = "重置对话" },
            new FakeHandler { CommandName = "help", Description = "帮助" }
        });

        Assert.Equal(2, registry.Commands.Count);
        var newInfo = Assert.Single(registry.Commands, c => c.Name == "new");
        Assert.Equal(new[] { "clear", "reset" }, newInfo.Aliases);
        Assert.Equal("重置对话", newInfo.Description);
    }
}
