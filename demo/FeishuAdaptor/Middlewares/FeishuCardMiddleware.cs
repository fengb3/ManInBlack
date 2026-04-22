using System.Runtime.CompilerServices;
using FeishuAdaptor.FeishuCard.CardViews;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;

namespace FeishuAdaptor.Middlewares;

/// <summary>
/// 飞书卡片渲染中间件 — 将 AI 流式响应实时渲染为飞书卡片。
/// <para>Reasoning → 折叠面板, Tool Call → 独立工具卡片 + 后续 TextContent 写入新卡片, 直接输出 → Markdown 元素</para>
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

        string lastLlmType = "";

        LlmOutputViewModel? lastOutput = null;
        LlmReasoningViewModel? lastReasoning = null;
        var toolExecutions = new Dictionary<string, LlmToolExecutionViewModel>();

        // 跟踪已创建的卡片视图，用于流式结束后关闭和释放
        List<CardViewBase> cardViews = [];

        await foreach (var update in next().WithCancellation(ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent r:
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
                        // 如果之前有 tool call，强制创建新卡片
                        if (lastLlmType != nameof(LlmOutputViewModel))
                        {
                            var (vm2, view2) = CreateCard<LlmOutputViewModel>(openId);
                            cardViews.Add(view2);
                            lastOutput = vm2;
                            lastLlmType = nameof(LlmOutputViewModel);
                        }

                        lastOutput!.Output += t.Text;
                        break;
                    case FunctionCallContent fcc:
                    {
                        lastLlmType = nameof(LlmToolExecutionViewModel);

                        if (!toolExecutions.TryGetValue(fcc.CallId, out var toolExecVm))
                        {
                            var (vm3, view3) = CreateCard<LlmToolExecutionViewModel>(openId);
                            cardViews.Add(view3);
                            toolExecVm = vm3;
                            toolExecutions[fcc.CallId] = toolExecVm;
                        }

                        toolExecVm.ToolName = fcc.Name;
                        toolExecVm.Arguments =
                            string.Join(", ", fcc.Arguments?.Select(pair => $"{pair.Key}: {pair.Value} ") ?? []);

                        break;
                    }
                    case FunctionResultContent frc:
                    {
                        lastLlmType = nameof(LlmToolExecutionViewModel);

                        if (!toolExecutions.TryGetValue(frc.CallId, out var toolExecVm))
                        {
                            var (vm4, view4) = CreateCard<LlmToolExecutionViewModel>(openId);
                            cardViews.Add(view4);
                            toolExecVm = vm4;
                            toolExecutions[frc.CallId] = toolExecVm;
                        }

                        var resultText = frc.Result?.ToString() ?? "";

                        if (resultText.Length > 200)
                            resultText = string.Concat(resultText.AsSpan(0, 200), "\n...");

                        toolExecVm.Result = resultText;
                        break;
                    }
                }
            }

            yield return update;
        }

        await Task.Delay(1000, ct);
        //
        // // 流式结束 — 关闭每张卡片的流式模式并释放资源
        // foreach (var view in cardViews)
        // {
        //     try
        //     {
        //         await view.CloseStreamingAsync(ct);
        //     }
        //     catch
        //     {
        //         // 关闭失败不影响整体流程
        //     }
        //     finally
        //     {
        //         view.Dispose();
        //     }
        // }
    }

    private (T ViewModel, CardView<T> View) CreateCard<T>(string userOpenId) where T : ViewModelBase
    {
        var view = serviceProvider.GetRequiredService<CardView<T>>();
        view.InitializeAsync().GetAwaiter().GetResult();
        view.SendToUserAsync("open_id", userOpenId).GetAwaiter().GetResult();
        return (view.ViewModel, view);
    }
}