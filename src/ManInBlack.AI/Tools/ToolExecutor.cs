using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Mcp;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Tools;

/// <summary>
/// 工具执行器：按 ToolName 派发。先查静态 handler 字典（源生成器生成的 [AiTool] handler），
/// 未命中时 fallback 到 <see cref="IMcpToolProvider"/>（MCP 工具）。MCP 执行路径内联包
/// <see cref="AgentLifecycleFilter"/>，复用本地工具的事件链（飞书卡片、audit hook、阻断）。
/// </summary>
public sealed class ToolExecutor : IToolExecutor
{
    private readonly ConcurrentDictionary<string, IToolHandler> _handlers;
    private readonly IMcpToolProvider? _mcpProvider;

    public ToolExecutor(IEnumerable<IToolHandler> handlers, IMcpToolProvider? mcpProvider = null)
    {
        _handlers = new(handlers.ToDictionary(h => h.ToolName));
        _mcpProvider = mcpProvider;
    }

    public void Register(IToolHandler handler)
        => _handlers[handler.ToolName] = handler;

    public async Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct)
    {
        try
        {
            if (_handlers.TryGetValue(ctx.ToolName, out var handler))
            {
                await handler.ExecuteAsync(ctx, ct);
            }
            else if (_mcpProvider is not null && _mcpProvider.IsMcpTool(ctx.ToolName))
            {
                await ExecuteMcpAsync(ctx, ct);
            }
            else
            {
                throw new ArgumentException($"Unknown tool: '{ctx.ToolName}'.");
            }
        }
        catch (Exception ex)
        {
            ctx.Error = ex;
        }
    }

    /// <summary>
    /// MCP 工具执行：内联包本地工具同款 filter 链（LoggingFilter → AgentLifecycleFilter，从请求 scope 取），
    /// 保证 MCP 工具与本地工具在日志/事件/阻断上行为一致。最内层调用 <see cref="IMcpToolProvider.ExecuteAsync"/>。
    /// </summary>
    private async Task ExecuteMcpAsync(ToolExecuteContext ctx, CancellationToken ct)
    {
        var sp = ctx.ServiceProvider;
        var logging = sp.GetService<LoggingFilter>();
        var lifecycle = sp.GetService<AgentLifecycleFilter>();

        Func<ToolExecuteContext, Task> core = async c =>
        {
            c.Result = await _mcpProvider!.ExecuteAsync(ctx.ToolName, c.Arguments, ct);
        };

        // 按本地工具 filter 链顺序包裹（外 → 内）：LoggingFilter → AgentLifecycleFilter → core
        // 注意：每步用局部变量捕获当前 pipeline 快照，避免闭包捕获被重新赋值的变量导致无限递归
        Func<ToolExecuteContext, Task> pipeline = core;
        if (lifecycle is not null)
        {
            var inner = pipeline;
            pipeline = c => lifecycle.ExecuteAsync(c, inner);
        }
        if (logging is not null)
        {
            var inner = pipeline;
            pipeline = c => logging.ExecuteAsync(c, inner);
        }

        await pipeline(ctx);
    }
}
