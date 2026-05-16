using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Tools;

namespace ManInBlack.AI.Tools;

public sealed class ToolExecutor : IToolExecutor
{
    private readonly ConcurrentDictionary<string, IToolHandler> _handlers;

    public ToolExecutor(IEnumerable<IToolHandler> handlers)
    {
        _handlers = new(handlers.ToDictionary(h => h.ToolName));
    }

    public void Register(IToolHandler handler)
        => _handlers[handler.ToolName] = handler;

    public async Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(ctx.ToolName, out var handler))
            throw new ArgumentException($"Unknown tool: '{ctx.ToolName}'.");
        await handler.ExecuteAsync(ctx, ct);
    }
}
