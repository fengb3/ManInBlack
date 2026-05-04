using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 钩子中间件，在 LLM 调用前和 Agent 循环结束时执行用户自定义钩子脚本。
/// <para>
/// 洋葱模型管道中位于 AgentLoopMiddleware 外层，因此 <c>next()</c> 会触发整个内部循环，
/// 仅当 AgentLoopMiddleware 的 while 循环退出（无更多 function call）时才返回。
/// </para>
/// </summary>
[ServiceRegister.Scoped]
public class HookMiddleware(IHookExecutor hookExecutor, ILogger<HookMiddleware> logger) : AgentMiddleware
{
    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // ── BeforeLlmCall：在首次 LLM 调用前执行钩子 ──
        logger.LogInformation("[HookMiddleware] BeforeLlmCall 触发，AgentId={AgentId}, UserInput={UserInput}",
            context.AgentId, context.UserInput);

        var beforeContext = new HookContext
        {
            HookPoint    = HookPoint.BeforeLlmCall.ToString(),
            AgentId      = context.AgentId,
            SystemPrompt = context.SystemPrompt,
            UserInput    = context.UserInput,
        };

        var beforeResult = await hookExecutor.ExecuteAsync(HookPoint.BeforeLlmCall, beforeContext, ct);

        // 如果钩子注入了文本，追加到 SystemPrompt（InjectTarget 为空时默认追加到 SystemPrompt）
        if (beforeResult.Succeeded
            && !string.IsNullOrEmpty(beforeResult.InjectedText)
            && (string.IsNullOrEmpty(beforeResult.InjectTarget)
                || string.Equals(beforeResult.InjectTarget, "SystemPrompt", StringComparison.OrdinalIgnoreCase)))
        {
            context.SystemPrompt = string.IsNullOrEmpty(context.SystemPrompt)
                ? beforeResult.InjectedText
                : $"{context.SystemPrompt}\n\n{beforeResult.InjectedText}";

            logger.LogInformation("[HookMiddleware] SystemPrompt 已注入文本，长度={Length}", beforeResult.InjectedText.Length);
        }

        // ── 流式转发内部管道输出，同时检测 FunctionCallContent ──
        var hasFunctionCalls = false;

        await foreach (var update in next().WithCancellation(ct))
        {
            if (!hasFunctionCalls)
            {
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent)
                    {
                        hasFunctionCalls = true;
                        break;
                    }
                }
            }

            yield return update;
        }

        // ── AgentCompleted：当 AgentLoopMiddleware 退出 while 循环（无 function call）时触发 ──
        if (!hasFunctionCalls)
        {
            logger.LogInformation("[HookMiddleware] AgentCompleted 触发，AgentId={AgentId}", context.AgentId);
            var completedContext = new HookContext
            {
                HookPoint    = HookPoint.AgentCompleted.ToString(),
                AgentId      = context.AgentId,
                SystemPrompt = context.SystemPrompt,
                UserInput    = context.UserInput,
            };

            await hookExecutor.ExecuteAsync(HookPoint.AgentCompleted, completedContext, ct);
        }
    }
}
