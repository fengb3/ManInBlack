namespace ManInBlack.AI.Core.Tools;

public interface IToolExecutor
{ 
    Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct = default);
}