namespace ManInBlack.AI.Core.Tools;

public abstract class ToolCallFilter
{
    public abstract Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next);
}