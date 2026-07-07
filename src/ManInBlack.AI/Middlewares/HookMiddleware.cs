using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 钩子中间件，通过 EventBus 订阅 Agent 生命周期事件并执行用户自定义钩子脚本。
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
        var key = context.AgentId;
        var bus = context.ServiceProvider.GetRequiredService<EventBus>();
        var subs = new List<IDisposable>();

        // ── 构建通用属性字典，所有 HookContext 共享 ──
        var props = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(context.RootUserId)) props["RootUserId"] = context.RootUserId;
        if (!string.IsNullOrEmpty(context.SessionId))  props["SessionId"]  = context.SessionId;
        if (!string.IsNullOrEmpty(context.ParentId))   props["ParentId"]   = context.ParentId;
        if (!string.IsNullOrEmpty(context.ParentType)) props["ParentType"] = context.ParentType;
        if (!string.IsNullOrEmpty(context.AgentName))  props["AgentName"]  = context.AgentName;

        // ── 订阅全部生命周期事件 ──
        subs.Add(bus.Subscribe<BeforeLlmCallEvent>(EventBus.HookKey(key), async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.BeforeLlmCall.ToString(),
                AgentId = evt.AgentId,
                SystemPrompt = evt.SystemPrompt,
                UserInput = evt.UserInput,
                Properties = props,
            };
            var result = await hookExecutor.ExecuteAsync(HookPoint.BeforeLlmCall, hookCtx, ct);
            if (result.Succeeded && !string.IsNullOrEmpty(result.InjectedText))
            {
                evt.InjectedTexts.Add(result.InjectedText);
                if (result.InjectTarget is not null)
                    evt.InjectTarget = result.InjectTarget;
            }
        }));

        subs.Add(bus.Subscribe<AfterLlmCallEvent>(EventBus.HookKey(key), async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.AfterLlmCall.ToString(),
                AgentId = evt.AgentId,
                SystemPrompt = evt.SystemPrompt,
                UserInput = evt.UserInput,
                Properties = props,
            };
            await hookExecutor.ExecuteAsync(HookPoint.AfterLlmCall, hookCtx, ct);
        }));

        subs.Add(bus.Subscribe<BeforeToolExecuteEvent>(EventBus.HookKey(key), async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.BeforeToolExecute.ToString(),
                AgentId = evt.AgentId,
                ToolName = evt.ToolName,
                CallId = evt.CallId,
                ArgumentsJson = evt.ArgumentsJson,
                Properties = props,
            };
            var result = await hookExecutor.ExecuteAsync(HookPoint.BeforeToolExecute, hookCtx, ct);
            if (result.IsBlocked)
            {
                evt.IsBlocked = true;
                evt.BlockReason = result.BlockReason;
            }
        }));

        subs.Add(bus.Subscribe<AfterToolExecuteEvent>(EventBus.HookKey(key), async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.AfterToolExecute.ToString(),
                AgentId = evt.AgentId,
                ToolName = evt.ToolName,
                CallId = evt.CallId,
                ArgumentsJson = evt.ArgumentsJson,
                ResultJson = evt.ResultJson,
                Error = evt.Error,
                Properties = props,
            };
            await hookExecutor.ExecuteAsync(HookPoint.AfterToolExecute, hookCtx, ct);
        }));

        subs.Add(bus.Subscribe<AllToolsCompletedEvent>(EventBus.HookKey(key), async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.AllToolsCompleted.ToString(),
                AgentId = evt.AgentId,
                Properties = props,
            };
            await hookExecutor.ExecuteAsync(HookPoint.AllToolsCompleted, hookCtx, ct);
        }));

        subs.Add(bus.Subscribe<AgentCompletedEvent>(EventBus.HookKey(key), async (evt, ct) =>
        {
            var hookCtx = new HookContext
            {
                HookPoint = HookPoint.AgentCompleted.ToString(),
                AgentId = evt.AgentId,
                SystemPrompt = evt.SystemPrompt,
                UserInput = evt.UserInput,
                Properties = props,
            };
            await hookExecutor.ExecuteAsync(HookPoint.AgentCompleted, hookCtx, ct);
        }));

        // ── BeforeLlmCall：发布事件，handler 执行钩子，读取注入文本 ──
        var beforeEvt = new BeforeLlmCallEvent
        {
            AgentId = key,
            SystemPrompt = context.SystemPrompt,
            UserInput = context.UserInput,
        };
        await bus.PublishAsync(EventBus.HookKey(key), beforeEvt, ct);

        if (beforeEvt.InjectedTexts.Count > 0
            && (string.IsNullOrEmpty(beforeEvt.InjectTarget)
                || string.Equals(beforeEvt.InjectTarget, "SystemPrompt", StringComparison.OrdinalIgnoreCase)))
        {
            var injected = string.Join(Environment.NewLine, beforeEvt.InjectedTexts);
            context.SystemPrompt = string.IsNullOrEmpty(context.SystemPrompt)
                ? injected
                : $"{context.SystemPrompt}\n\n{injected}";

            logger.LogDebug("[HookMiddleware] SystemPrompt 已注入文本，长度={Length}", injected.Length);
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

        // ── AgentCompleted：当无 function call 时发布事件 ──
        if (!hasFunctionCalls)
        {
            logger.LogDebug("[HookMiddleware] AgentCompleted 触发，AgentId={AgentId}", context.AgentId);
            await bus.PublishAsync(EventBus.HookKey(key), new AgentCompletedEvent
            {
                AgentId = key,
                SystemPrompt = context.SystemPrompt,
                UserInput = context.UserInput,
            }, ct);
        }

        // ── 清理订阅 ──
        foreach (var sub in subs) sub.Dispose();
    }
}
