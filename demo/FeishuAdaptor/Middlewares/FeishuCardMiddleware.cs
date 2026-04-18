using System.Runtime.CompilerServices;
using System.Text;
using FeishuAdaptor.FeishuCard;
using FeishuAdaptor.FeishuCard.CardViews;
using FeishuAdaptor.FeishuCard.Cards;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;

namespace FeishuAdaptor.Middlewares;

/// <summary>
/// 飞书卡片渲染中间件 — 将 AI 流式响应实时渲染为飞书卡片。
/// <para>Reasoning → 折叠面板, Tool Call → 独立卡片, 直接输出 → Markdown 元素</para>
/// </summary>
[ServiceRegister.Scoped]
public class FeishuCardMiddleware(
    IServiceProvider serviceProvider,
    CardService cardService,
    CardUpdateScheduler scheduler
) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var openId = context.ParentId;
        var initialized = false;

        string lastLlmType = ""; // can be reasoning and output
        
        LlmOutputViewModel? lastOutput = null;
        LlmReasoningViewModel? lastReasoning = null;
        Dictionary<string, LlmToolExecutionViewModel> toolExecutions = new Dictionary<string, LlmToolExecutionViewModel>();

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
                            lastReasoning = await CreateCard<LlmReasoningViewModel>(openId);
                            lastLlmType = nameof(LlmReasoningViewModel);
                        }
                        lastReasoning!.Reasoning += r.Text;
                        break;
                    case TextContent t:
                        if (string.IsNullOrEmpty(t.Text))
                            break;
                        if (lastLlmType != nameof(LlmOutputViewModel))
                        {
                            lastOutput = await CreateCard<LlmOutputViewModel>(openId);
                            lastLlmType = nameof(LlmOutputViewModel);
                        }
                        lastOutput!.Output += t.Text;
                        break;
                    case FunctionCallContent fcc:
                    {
                        lastLlmType = nameof(LlmToolExecutionViewModel);
                        
                        if (!toolExecutions.TryGetValue(fcc.CallId, out var toolExecVm))
                        {
                            toolExecVm = await CreateCard<LlmToolExecutionViewModel>(openId);
                            toolExecutions[fcc.CallId] = toolExecVm;
                        }

                        toolExecVm!.ToolName = fcc.Name;
                        toolExecVm!.Arguments =
                            string.Join(", ", fcc.Arguments.Select(pair => $"{pair.Key}: {pair.Value}"));

                        break;
                    }
                    case FunctionResultContent frc:
                    {
                        if (!toolExecutions.TryGetValue(frc.CallId, out var toolExecVm))
                        {
                            toolExecVm = await CreateCard<LlmToolExecutionViewModel>(openId);
                            toolExecutions[frc.CallId] = toolExecVm;
                        }
                        
                        var resultText = frc.Result.ToString() ?? "";
                        
                        if (resultText.Length > 200)
                        {
                            resultText = string.Concat(resultText.AsSpan(0, 200), "\n ... "); // 前200个字符
                        }
                            
                        toolExecVm!.Result = resultText; 
                        break;
                    }
                }
            }

            yield return update;
        }
    }

    private async Task<T> CreateCard<T>(string userOpenId) where T : ViewModelBase
    {
        var view = serviceProvider.GetRequiredService<CardView<T>>();
        await view.InitializeAsync();
        await view.SendToUserAsync("open_id", userOpenId);
        return view.ViewModel;
    }
}
