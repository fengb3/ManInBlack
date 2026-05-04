using System.Text.Json;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.ToolCallFilters;

/// <summary>
/// 工具调用级别的钩子过滤器。
/// 在工具执行前后分别触发 BeforeToolExecute / AfterToolExecute 钩子，
/// 并支持通过钩子返回的 IsBlocked 阻断工具执行。
/// </summary>
[ServiceRegister.Scoped]
public class HookFilter(IHookExecutor hookExecutor, ILogger<HookFilter> logger) : ToolCallFilter
{
    private readonly IHookExecutor _hookExecutor = hookExecutor;

    /// <inheritdoc />
    public override async Task ExecuteAsync(
        ToolExecuteContext context,
        Func<ToolExecuteContext, Task> next
    )
    {
        // ── Before 钩子 ──
        logger.LogInformation("[HookFilter] BeforeToolExecute 触发，ToolName={ToolName}, CallId={CallId}",
            context.ToolName, context.CallId);
        var beforeCtx = new HookContext
        {
            HookPoint = HookPoint.BeforeToolExecute.ToString(),
            ToolName = context.ToolName,
            CallId = context.CallId,
            ArgumentsJson = context.Arguments is not null
                ? JsonSerializer.Serialize(context.Arguments)
                : null,
        };

        var beforeResult = await _hookExecutor.ExecuteAsync(
            HookPoint.BeforeToolExecute,
            beforeCtx,
            default
        );

        // 钩子阻断：设置错误并跳过实际工具调用
        if (beforeResult.IsBlocked)
        {
            logger.LogWarning("[HookFilter] 工具 {ToolName} 被钩子阻断：{Reason}", context.ToolName, beforeResult.BlockReason);
            context.Error = new InvalidOperationException(
                beforeResult.BlockReason ?? "Hook blocked execution"
            );
            return;
        }

        // ── 执行实际工具调用 ──
        await next(context);

        // ── After 钩子（fire-and-forget，不阻断流程） ──
        var afterCtx = new HookContext
        {
            HookPoint = HookPoint.AfterToolExecute.ToString(),
            ToolName = context.ToolName,
            CallId = context.CallId,
            ArgumentsJson = context.Arguments is not null
                ? JsonSerializer.Serialize(context.Arguments)
                : null,
            ResultJson = context.Result?.ToString(),
            Error = context.Error?.Message,
        };

        // 执行 After 钩子，忽略其返回结果，不阻断流程
        logger.LogInformation("[HookFilter] AfterToolExecute 触发，ToolName={ToolName}", context.ToolName);
        await _hookExecutor.ExecuteAsync(HookPoint.AfterToolExecute, afterCtx, default);
    }
}
