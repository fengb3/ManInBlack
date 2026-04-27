using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Services;

namespace ManInBlack.AI.ToolCallFilters;

[ServiceRegister.Scoped]
public class BroadCastingFilter(EventBus eventBus) : ToolCallFilter
{
    public override async Task ExecuteAsync(
        ToolExecuteContext context,
        Func<ToolExecuteContext, Task> next
    )
    {
        await eventBus.PublishAsync(
            new ToolExecutingEvent(context.CallId, context.ToolName, context.Arguments)
        );

        await next(context);

        await eventBus.PublishAsync(
            new ToolExecutedEvent(context.CallId, context.Result, context.Error)
        );
    }
}

public record ToolExecutingEvent(
    string CallId,
    string ToolName,
    IDictionary<string, object?>? Arguments,
    Dictionary<string, object>? Items = null
);

public record ToolExecutedEvent(
    string CallId,
    object? Result,
    Exception? Exception = null,
    Dictionary<string, object>? Items = null
);
