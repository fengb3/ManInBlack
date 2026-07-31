using FeishuAdaptor.Tools;
using Xunit;

namespace FeishuAdaptor.Tests;

public class PendingAskRegistryTests
{
    private static PendingAsk NewAsk(out TaskCompletionSource<AskUserResult> tcs)
    {
        tcs = new TaskCompletionSource<AskUserResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        return new PendingAsk
        {
            Tcs = tcs,
            MultiSelect = false,
            OptionsByValue = new Dictionary<string, AskUserOption>(),
            AskedUserId = "u1",
        };
    }

    [Fact]
    public void Register_And_TryGet()
    {
        var reg = new PendingAskRegistry();
        var ask = NewAsk(out _);
        reg.Register("r1", ask);
        Assert.True(reg.TryGet("r1", out var got));
        Assert.Same(ask, got);
    }

    [Fact]
    public void Resolve_Completes_Tcs_And_Removes_Entry()
    {
        var reg = new PendingAskRegistry();
        var ask = NewAsk(out var tcs);
        reg.Register("r1", ask);

        Assert.True(reg.Resolve("r1", new AskUserResult(new[] { "yes" })));
        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.False(reg.TryGet("r1", out _));
    }

    [Fact]
    public async Task Resolve_Is_Idempotent_On_Duplicate()
    {
        var reg = new PendingAskRegistry();
        var ask = NewAsk(out var tcs);
        reg.Register("r1", ask);

        Assert.True(reg.Resolve("r1", new AskUserResult(new[] { "yes" })));
        Assert.False(reg.Resolve("r1", new AskUserResult(new[] { "no" })));

        var completed = await tcs.Task;
        Assert.Equal(new[] { "yes" }, completed.SelectedValues);
    }

    [Fact]
    public void Resolve_Unknown_RequestId_Returns_False()
    {
        var reg = new PendingAskRegistry();
        Assert.False(reg.Resolve("nope", new AskUserResult(Array.Empty<string>())));
    }
}
