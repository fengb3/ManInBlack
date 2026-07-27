using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Commands;
using ManInBlack.AI.Tests.Helpers;
using Xunit;

namespace ManInBlack.AI.Tests.Commands;

public class SlashCommandItemsTests
{
    [Fact]
    public void GetCommandArgs_ReturnsEmpty_WhenNotSet()
    {
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider);
        Assert.Empty(ctx.GetCommandArgs());
    }

    [Fact]
    public void GetCommandArgs_ReturnsInjectedArgs()
    {
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider);
        ctx.Items[SlashCommandItems.Args] = new[] { "a", "b" };

        Assert.Equal(new[] { "a", "b" }, ctx.GetCommandArgs());
    }
}
