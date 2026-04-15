namespace ManInBlack.AI.Tools;

public interface IToolExecutor
{ 
    Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct = default);
}