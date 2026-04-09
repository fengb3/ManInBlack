using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tools;

/// <summary>
/// marks a method is an AI Function, this attribute is only a mark for source generator
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ToolAttribute : Attribute
{

}

/// <summary>
/// marks an AI Function that applies a filter
/// </summary>
/// <typeparam name="T">filter type</typeparam>
public class ToolCallFilterAttribute<T> : Attribute where T : ToolCallFilter
{
    
}

public abstract class ToolCallFilter
{
    public abstract Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next);
}

public class ToolExecuteContext(IServiceProvider provider)
{
    public IServiceProvider ServiceProvider { get; } =  provider;
    
    public AIFunctionDeclaration AIFunctionDeclaration { get; set; }
    
    public Dictionary<string, object?> Arguments { get; set; }
    
    public object? Result { get; set; }
    
    public int Index { get; set; }
    
    public string CallId { get; set; }
    
    public Exception Error { get; set; }
}