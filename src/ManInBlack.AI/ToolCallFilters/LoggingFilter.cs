using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Tools;
using Microsoft.Extensions.Logging;

namespace AgentConsole.Tools;

[ServiceRegister.Scoped]
public class LoggingFilter(ILogger<LoggingFilter> logger) : ToolCallFilter
{
    public override async Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next)
    {
        var arguments = context.Arguments.Select(pair => $"{pair.Key}: {pair.Value}").ToArray();
        
        // set console color for better visibility
        
        logger.LogInformation("Executing {toolName} {arguments}", context.ToolName , string.Join(", ", arguments));
        
        await next(context);
        
        logger.LogInformation("Executed {toolName} {resultLength}", context.ToolName ,context.Result.ToString().Length);
       
    }
}