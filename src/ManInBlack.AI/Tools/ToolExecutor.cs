using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Mcp;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Tools;

/// <summary>
/// 工具执行器：按 ToolName 派发。先查静态 handler 字典（源生成器生成的 [AiTool] handler），
/// 未命中时 fallback 到 <see cref="IMcpToolProvider"/>（MCP 工具）。
/// 两条路径都包裹同一 filter 链（LoggingFilter → AgentLifecycleFilter，从请求 scope 取），
/// 保证本地工具与 MCP 工具在日志/事件（Before/AfterToolExecuteEvent）/阻断上行为一致。
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
            Func<ToolExecuteContext, Task> core;
            if (_handlers.TryGetValue(ctx.ToolName, out var handler))
            {
                var h = handler;
                core = async c => await h.ExecuteAsync(c, ct);
            }
            else if (_mcpProvider is not null && _mcpProvider.IsMcpTool(ctx.ToolName))
            {
                var mcp = _mcpProvider;
                core = async c => { c.Result = await mcp.ExecuteAsync(c.ToolName, c.Arguments, ct); };
            }
            else
            {
                throw new ArgumentException($"Unknown tool: '{ctx.ToolName}'.");
            }

            await BuildFilterPipeline(ctx, core)(ctx);
        }
        catch (Exception ex)
        {
            ctx.Error = ex;
        }
    }

    /// <summary>
    /// 组装 filter 链（外 → 内）：LoggingFilter → AgentLifecycleFilter → core。
    /// 每步用局部变量捕获当前 pipeline 快照，避免闭包捕获被重新赋值的变量导致无限递归。
    /// </summary>
    private Func<ToolExecuteContext, Task> BuildFilterPipeline(
        ToolExecuteContext ctx, Func<ToolExecuteContext, Task> core)
    {
        var sp = ctx.ServiceProvider;
        var logging = sp.GetService<LoggingFilter>();
        var lifecycle = sp.GetService<AgentLifecycleFilter>();

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
        return pipeline;
    }
}
