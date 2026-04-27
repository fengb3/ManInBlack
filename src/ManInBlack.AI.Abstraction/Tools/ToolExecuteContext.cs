using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Tools;

public class ToolExecuteContext(IServiceProvider provider)
{
    public IServiceProvider ServiceProvider { get; } =  provider;

    public AIFunctionDeclaration? AIFunctionDeclaration { get; set; }

    public string ToolName { get; set; } = string.Empty;

    public IDictionary<string, object?>? Arguments { get; set; }

    public object? Result { get; set; }

    public int Index { get; set; }

    public string CallId { get; set; } = string.Empty;

    public Exception? Error { get; set; }
}