using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FeishuAdaptor.FeishuCard.CardViews;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Services;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.AI;

namespace FeishuAdaptor.Middlewares;

/// <summary>
/// 飞书卡片渲染中间件 — 将 AI 流式响应实时渲染为飞书卡片。
/// <para>Reasoning → 折叠面板, Tool Call → 独立卡片, 直接输出 → Markdown 元素</para>
/// </summary>
[ServiceRegister.Scoped]
public class FeishuCardMiddleware(
    IServiceProvider serviceProvider
) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var openId = context.ParentId;

        string lastLlmType = ""; // can be reasoning and output

        LlmOutputViewModel? lastOutput = null;
        LlmReasoningViewModel? lastReasoning = null;
        Dictionary<string, LlmToolExecutionViewModel> toolExecutions = new Dictionary<string, LlmToolExecutionViewModel>();

        // 跟踪已创建的卡片视图，用于流式结束后关闭和释放
        List<object> cardViews = [];

        await foreach (var update in next().WithCancellation(ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent r:
                        // cardView.ViewModel.AppendReasoning(r.Text);
                        if (lastLlmType != nameof(LlmReasoningViewModel))
                        {
                            var (vm1, view1) = CreateCard<LlmReasoningViewModel>(openId);
                            cardViews.Add(view1);
                            lastReasoning = vm1;
                            lastLlmType = nameof(LlmReasoningViewModel);
                        }
                        lastReasoning!.Reasoning += r.Text;
                        break;
                    case TextContent t:
                        if (string.IsNullOrEmpty(t.Text))
                            break;
                        if (lastLlmType != nameof(LlmOutputViewModel))
                        {
                            var (vm2, view2) = CreateCard<LlmOutputViewModel>(openId);
                            cardViews.Add(view2);
                            lastOutput = vm2;
                            lastLlmType = nameof(LlmOutputViewModel);
                        }
                        lastOutput!.Output += t.Text;
                        break;
                    // case FunctionCallContent fcc:
                    // {
                    //     lastLlmType = nameof(LlmToolExecutionViewModel);
                    //
                    //     if (!toolExecutions.TryGetValue(fcc.CallId, out var toolExecVm))
                    //     {
                    //         var (vm3, view3) = CreateCard<LlmToolExecutionViewModel>(openId);
                    //         cardViews.Add(view3);
                    //         toolExecVm = vm3;
                    //         toolExecutions[fcc.CallId] = toolExecVm;
                    //     }
                    //
                    //     toolExecVm.ToolName = fcc.Name;
                    //     toolExecVm.Arguments =
                    //         string.Join(", ", fcc.Arguments.Select(pair => $"{pair.Key}: {pair.Value}"));
                    //
                    //     break;
                    // }
                    // case FunctionResultContent frc:
                    // {
                    //     if (!toolExecutions.TryGetValue(frc.CallId, out var toolExecVm))
                    //     {
                    //         var (vm4, view4) = CreateCard<LlmToolExecutionViewModel>(openId);
                    //         cardViews.Add(view4);
                    //         toolExecVm = vm4;
                    //         toolExecutions[frc.CallId] = toolExecVm;
                    //     }
                    //
                    //     var resultText = frc.Result.ToString() ?? "";
                    //
                    //     if (resultText.Length > 200)
                    //     {
                    //         resultText = string.Concat(resultText.AsSpan(0, 200), "\n ... "); // 前200个字符
                    //     }
                    //
                    //     toolExecVm.Result = resultText;
                    //     break;
                    // }
                }
            }

            yield return update;
        }

        // 流式结束 — 关闭每张卡片的流式模式并释放资源
        foreach (var view in cardViews)
        {
            try
            {
                if (view is CardView<ViewModelBase> cv)
                {
                    await cv.CloseStreamingAsync(ct);
                }
            }
            catch
            {
                // 关闭失败不影响整体流程
            }
            finally
            {
                ((IDisposable)view).Dispose();
            }
        }
    }

    private (T ViewModel, CardView<T> View) CreateCard<T>(string userOpenId) where T : ViewModelBase
    {
        var view = serviceProvider.GetRequiredService<CardView<T>>();
        view.InitializeAsync().GetAwaiter().GetResult();
        view.SendToUserAsync("open_id", userOpenId).GetAwaiter().GetResult();
        return (view.ViewModel, view);
    }
}

/// <summary>
/// 飞书工具卡片中间件 — 订阅 EventBus 中的工具执行事件，实时渲染为飞书卡片。
/// <para>ToolExecutingEvent → 创建 ToolExecutionCardView, ToolExecuted → 更新执行结果</para>
/// </summary>
[ServiceRegister.Scoped]
public class FeishuToolCardMiddleware(
    IServiceProvider serviceProvider,
    EventBus eventBus
) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var openId = context.ParentId;
        var cards = new ConcurrentDictionary<string, CardView<LlmToolExecutionViewModel>>();

        // 订阅工具执行开始事件 → 创建卡片
        Task OnToolExecuting(ToolExecutingEvent evt, CancellationToken _)
        {
            var (vm, view) = CreateCard(openId);
            vm.ToolName = evt.ToolName;
            vm.Arguments = evt.Arguments is null
                ? "\n"
                : string.Join("\n", evt.Arguments.Select(p => $"- **{p.Key}**: {p.Value}"));
            cards[evt.CallId] = view;
            return Task.CompletedTask;
        }

        // 订阅工具执行完成事件 → 更新结果
        Task OnToolExecuted(ToolExecutedEvent evt, CancellationToken _)
        {
            if (!cards.TryGetValue(evt.CallId, out var view)) return Task.CompletedTask;

            var resultText = evt.Exception is not null
                ? $"❌ {evt.Exception.Message}"
                : evt.Result?.ToString() ?? "";

            if (resultText.Length > 200)
                resultText = string.Concat(resultText.AsSpan(0, 200), "\n...");

            view.ViewModel.Result = resultText;
            return Task.CompletedTask;
        }

        using var subExecuting = eventBus.Subscribe<ToolExecutingEvent>(OnToolExecuting);
        using var subExecuted = eventBus.Subscribe<ToolExecutedEvent>(OnToolExecuted);

        await foreach (var update in next().WithCancellation(ct))
        {
            yield return update;
        }

        // 流式结束 — 关闭并释放所有工具卡片
        foreach (var view in cards.Values)
        {
            try
            {
                await view.CloseStreamingAsync(ct);
            }
            catch
            {
                // 关闭失败不影响整体流程
            }
            finally
            {
                view.Dispose();
            }
        }
    }

    private (LlmToolExecutionViewModel ViewModel, CardView<LlmToolExecutionViewModel> View) CreateCard(
        string userOpenId)
    {
        var view = serviceProvider.GetRequiredService<CardView<LlmToolExecutionViewModel>>();
        view.InitializeAsync().GetAwaiter().GetResult();
        view.SendToUserAsync("open_id", userOpenId).GetAwaiter().GetResult();
        return (view.ViewModel, view);
    }
}
