using FeishuAdaptor.FeishuCard;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;

namespace FeishuAdaptor.Tools;

/// <summary>向飞书用户提问并阻塞等待其卡片选择的工具。</summary>
[ServiceRegister.Scoped]
public partial class AskUserTool(
    CardService cardService,
    PendingAskRegistry registry,
    AgentContext agentContext)
{
    /// <summary>向当前飞书用户发送一张单选/多选卡片，阻塞等待用户选择后返回结果。</summary>
    /// <param name="question">要问用户的问题文本。</param>
    /// <param name="options">可选项列表。</param>
    /// <param name="multiSelect">是否允许多选，默认 false（单选）。</param>
    /// <param name="timeoutSeconds">等待超时秒数，默认 300；超时自动结束。</param>
    /// <returns>用户的选择（如「用户选择了：是」），或超时/取消/错误提示。</returns>
    [AiTool]
    public async Task<string> AskUserAsync(
        string question,
        List<AskUserOption> options,
        bool multiSelect = false,
        int timeoutSeconds = 300)
    {
        if (options is null || options.Count == 0)
            return "提问失败：未提供可选项";

        var userId = agentContext.RootUserId;
        var requestId = Guid.NewGuid().ToString("N");
        var card = AskUserCardBuilder.Build(question, options, multiSelect, requestId);

        var optionsByValue = options.ToDictionary(o => o.Value ?? o.Label, o => o);
        var tcs = new TaskCompletionSource<AskUserResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.Register(requestId, new PendingAsk
        {
            Tcs = tcs,
            MultiSelect = multiSelect,
            OptionsByValue = optionsByValue,
            AskedUserId = userId,
        });

        try
        {
            var cardId = await cardService.CreateAsync(card, agentContext.CancellationToken);
            await cardService.SendMessageAsync(cardId, "user_id", userId, agentContext.CancellationToken);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                agentContext.CancellationToken, timeoutCts.Token);

            var delay = Task.Delay(Timeout.Infinite, linkedCts.Token);
            var done = await Task.WhenAny(tcs.Task, delay);

            if (done == tcs.Task && tcs.Task.IsCompletedSuccessfully)
            {
                var result = await tcs.Task;
                return FormatSelection(optionsByValue, result);
            }

            return TerminalMessage(timeoutSeconds);
        }
        catch (OperationCanceledException)
        {
            return TerminalMessage(timeoutSeconds);
        }
        finally
        {
            registry.TryRemove(requestId, out _);
        }
    }

    private static string FormatSelection(IReadOnlyDictionary<string, AskUserOption> optionsByValue, AskUserResult result)
    {
        var labels = result.SelectedValues
            .Select(v => optionsByValue.TryGetValue(v, out var o) ? o.Label : v);
        return "用户选择了：" + string.Join("、", labels);
    }

    private string TerminalMessage(int timeoutSeconds) => agentContext.CancellationToken.IsCancellationRequested
        ? "提问已被取消（用户发起了新对话或会话结束）"
        : $"用户未在 {timeoutSeconds} 秒内作答（已超时）";
}
