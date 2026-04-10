namespace ManInBlack.AI.Tools;

public abstract class ToolCallFilter
{
    public abstract Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next);
}