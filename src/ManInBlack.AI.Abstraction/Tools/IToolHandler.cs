namespace ManInBlack.AI.Abstraction.Tools;

public interface IToolHandler
{
    string ToolName { get; }
    Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct = default);
}
