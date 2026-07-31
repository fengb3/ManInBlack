using FeishuAdaptor.FeishuCard;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Tools;
using ManInBlack.AI.Abstraction.Middleware;
using NSubstitute;
using System.Text.RegularExpressions;
using Xunit;

namespace FeishuAdaptor.Tests;

public class AskUserToolTests
{
    private static AskUserOption[] Opts => new[]
    {
        new AskUserOption("是") { Value = "yes" },
        new AskUserOption("否") { Value = "no" },
    };

    private static (AskUserTool tool, CardService card, PendingAskRegistry reg, AgentContext ctx) MakeTool(string userId = "u1")
    {
        var card = Substitute.For<CardService>(default!, default!, default!);
        var reg = new PendingAskRegistry();
        var ctx = new AgentContext(Substitute.For<IServiceProvider>()) { RootUserId = userId };
        var tool = new AskUserTool(card, reg, ctx);
        return (tool, card, reg, ctx);
    }

    private static string? ExtractRequestId(Card? card)
    {
        if (card is null) return null;
        var m = Regex.Match(card.ToJson(), "\"requestId\":\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    [Fact]
    public async Task Empty_Options_Returns_Failure_Without_Sending_Card()
    {
        var (tool, card, _, _) = MakeTool();
        var ret = await tool.AskUserAsync("q", new List<AskUserOption>(), false, 1);
        Assert.Contains("未提供可选项", ret);
        await card.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Resolved_Returns_Selected_Label()
    {
        var (tool, card, reg, _) = MakeTool();
        Card? captured = null;
        card.CreateAsync(Arg.Any<Card>(), Arg.Any<CancellationToken>()).Returns("card-1");
        card.When(x => x.CreateAsync(Arg.Any<Card>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<Card>());

        var task = Task.Run(() => tool.AskUserAsync("继续吗", new List<AskUserOption>(Opts), false, 30));
        while (captured is null) await Task.Delay(10);
        reg.Resolve(ExtractRequestId(captured)!, new AskUserResult(new[] { "yes" }));

        var ret = await task;
        Assert.Equal("用户选择了：是", ret);
    }

    [Fact]
    public async Task Timeout_Returns_Timeout_Message()
    {
        var (tool, card, _, _) = MakeTool();
        card.CreateAsync(Arg.Any<Card>(), Arg.Any<CancellationToken>()).Returns("card-1");
        var ret = await tool.AskUserAsync("q", new List<AskUserOption>(Opts), false, 0);
        Assert.Contains("超时", ret);
    }

    [Fact]
    public async Task Agent_Cancelled_Returns_Cancel_Message()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var card = Substitute.For<CardService>(default!, default!, default!);
        var reg = new PendingAskRegistry();
        var ctx = new AgentContext(Substitute.For<IServiceProvider>())
        { RootUserId = "u1", CancellationToken = cts.Token };
        var tool = new AskUserTool(card, reg, ctx);
        card.CreateAsync(Arg.Any<Card>(), Arg.Any<CancellationToken>()).Returns("card-1");

        var ret = await tool.AskUserAsync("q", new List<AskUserOption>(Opts), false, 30);
        Assert.Contains("取消", ret);
    }
}
