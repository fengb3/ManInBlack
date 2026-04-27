namespace ManInBlack.AI.Abstraction.Tools;

public abstract class ToolCallFilter
{
    public abstract Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next);
}