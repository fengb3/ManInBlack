using FeishuAdaptor.EventHandlers;
using FeishuAdaptor.Tools;
using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FeishuAdaptor.Tests;

public class CardActionCallbackHandlerTests
{
    private static CallbackV2Dto<CardActionTriggerEventBodyDto> SingleSelectInput(string requestId, string option)
    {
        var body = new CardActionTriggerEventBodyDto
        {
            Action = new CardActionTriggerEventBodyDto.ActionSuffix
            {
                Value = new Dictionary<string, object> { ["requestId"] = requestId, ["option"] = option },
            },
        };
        return new CallbackV2Dto<CardActionTriggerEventBodyDto> { Event = body };
    }

    private static CallbackV2Dto<CardActionTriggerEventBodyDto> MultiSelectInput(string requestId, string[] selected)
    {
        var body = new CardActionTriggerEventBodyDto
        {
            Action = new CardActionTriggerEventBodyDto.ActionSuffix
            {
                Value = new Dictionary<string, object> { ["requestId"] = requestId },
                FormValue = new Dictionary<string, object> { ["opts"] = selected },
            },
        };
        return new CallbackV2Dto<CardActionTriggerEventBodyDto> { Event = body };
    }

    private static PendingAskRegistry RegistryWith(string requestId, out TaskCompletionSource<AskUserResult> tcs, bool multiSelect = false)
    {
        var reg = new PendingAskRegistry();
        tcs = new TaskCompletionSource<AskUserResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        reg.Register(requestId, new PendingAsk
        {
            Tcs = tcs,
            MultiSelect = multiSelect,
            OptionsByValue = new Dictionary<string, AskUserOption>(),
            AskedUserId = "u1",
        });
        return reg;
    }

    [Fact]
    public async Task Single_Select_Resolves_With_Option_Value()
    {
        var reg = RegistryWith("rid1", out var tcs);
        var handler = new CardActionCallbackHandler(reg, Substitute.For<ILogger<CardActionCallbackHandler>>());

        var resp = await handler.ExecuteAsync(SingleSelectInput("rid1", "yes"), CancellationToken.None);

        Assert.NotNull(resp);
        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.Equal(new[] { "yes" }, (await tcs.Task).SelectedValues);
    }

    [Fact]
    public async Task Multi_Select_Resolves_With_Form_Values()
    {
        var reg = RegistryWith("rid2", out var tcs, multiSelect: true);
        var handler = new CardActionCallbackHandler(reg, Substitute.For<ILogger<CardActionCallbackHandler>>());

        var resp = await handler.ExecuteAsync(MultiSelectInput("rid2", new[] { "a", "b" }), CancellationToken.None);

        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.Equal(new[] { "a", "b" }, (await tcs.Task).SelectedValues);
    }

    [Fact]
    public async Task Unknown_RequestId_Does_Not_Throw_And_Leaves_Registry()
    {
        var reg = new PendingAskRegistry();
        var handler = new CardActionCallbackHandler(reg, Substitute.For<ILogger<CardActionCallbackHandler>>());

        var resp = await handler.ExecuteAsync(SingleSelectInput("unknown", "x"), CancellationToken.None);

        Assert.NotNull(resp);
        Assert.False(reg.Resolve("unknown", new AskUserResult(Array.Empty<string>())));
    }
}
