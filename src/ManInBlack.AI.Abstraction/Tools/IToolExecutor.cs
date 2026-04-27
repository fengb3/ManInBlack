namespace ManInBlack.AI.Abstraction.Tools;

public interface IToolExecutor
{ 
    Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct = default);
}