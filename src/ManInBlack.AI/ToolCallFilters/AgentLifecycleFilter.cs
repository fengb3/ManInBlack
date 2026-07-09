using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.ToolCallFilters;

/// <summary>
/// 工具调用生命周期过滤器，通过 EventBus 发布工具执行前后事件。
/// 支持通过 BeforeToolExecuteEvent.IsBlocked 阻断工具执行。
/// </summary>
[ServiceRegister.Scoped]
public class AgentLifecycleFilter(EventBus eventBus, ILogger<AgentLifecycleFilter> logger) : ToolCallFilter
{
    private static JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public override async Task ExecuteAsync(
        ToolExecuteContext context,
        Func<ToolExecuteContext, Task> next
    )
    {
        var agentContext = context.ServiceProvider.GetRequiredService<AgentContext>();
        var key = agentContext.AgentId;
        var argsJson = context.Arguments is not null
            ? JsonSerializer.Serialize(context.Arguments, jsonOptions)
            : null;

        // ── BeforeToolExecute 事件 ──
        var beforeEvt = new BeforeToolExecuteEvent
        {
            AgentId = key,
            ToolName = context.ToolName,
            CallId = context.CallId,
            ArgumentsJson = argsJson
        };

        var hookKey = EventBus.HookKey(key);
        // hook lane 先跑（await），读 IsBlocked；再 observer lane，让观察者看到带 IsBlocked 的事件
        await eventBus.PublishAsync(hookKey, beforeEvt, default);
        await eventBus.PublishAsync(key, beforeEvt, default);

        if (beforeEvt.IsBlocked)
        {
            logger.LogWarning("[AgentLifecycleFilter] 工具 {ToolName} 被阻断：{Reason}", context.ToolName,
                beforeEvt.BlockReason);
            context.Error = new InvalidOperationException(
                beforeEvt.BlockReason ?? "Blocked by AgentLifecycleFilter"
            );
            return;
        }

        // ── 执行实际工具调用 ──
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            context.Error = ex;
        }

        // ── AfterToolExecute 事件（hook lane + observer lane）──
        var afterEvt = new AfterToolExecuteEvent
        {
            AgentId = key,
            ToolName = context.ToolName,
            CallId = context.CallId,
            ArgumentsJson = argsJson,
            ResultJson = context.Result?.ToString(),
            Error = context.Error?.Message,
        };
        await eventBus.PublishAsync(hookKey, afterEvt, default);
        await eventBus.PublishAsync(key, afterEvt, default);
    }
}