using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Events;
using Xunit;

namespace ManInBlack.AI.Tests.Hooks;

public class CommandHookModelTests
{
    [Fact]
    public void HookPoint_HasAfterCommand()
        => Assert.Equal("AfterCommand", nameof(HookPoint.AfterCommand));

    [Fact]
    public void HookContext_CarriesCommandFields()
    {
        var ctx = new HookContext
        {
            CommandName = "new",
            CommandArgs = "[\"arg\"]",
            Succeeded = true,
        };
        Assert.Equal("new", ctx.CommandName);
        Assert.Equal("[\"arg\"]", ctx.CommandArgs);
        Assert.True(ctx.Succeeded);
    }

    [Fact]
    public void CommandExecutedEvent_DefaultsSucceededTrue()
    {
        var evt = new CommandExecutedEvent { CommandName = "new" };
        Assert.True(evt.Succeeded);
        Assert.Equal("new", evt.CommandName);
    }
}
