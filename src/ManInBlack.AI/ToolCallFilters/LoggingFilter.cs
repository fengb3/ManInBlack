using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.ToolCallFilters;

[ServiceRegister.Scoped]
public partial class LoggingFilter(ILogger<LoggingFilter> logger) : ToolCallFilter
{
    public override async Task ExecuteAsync(
        ToolExecuteContext context,
        Func<ToolExecuteContext, Task> next
    )
    {
        var arguments = context.Arguments!.Select(pair => $"{pair.Key}: {pair.Value}").ToArray();

        // set console color for better visibility

        LogExecutingToolNameArguments(logger, context.ToolName, string.Join(", ", arguments));

        await next(context);

        LogExecutedToolNameResultLength(
            logger,
            context.ToolName,
            context.Result?.ToString()?.Length ?? 0
        );
    }

    [LoggerMessage(LogLevel.Information, "[TOOL] Executing {toolName} {arguments}")]
    static partial void LogExecutingToolNameArguments(
        ILogger<LoggingFilter> logger,
        string toolName,
        string arguments
    );

    [LoggerMessage(
        LogLevel.Information,
        "[TOOL] Executed {toolName} with result have {resultLength} length"
    )]
    static partial void LogExecutedToolNameResultLength(
        ILogger<LoggingFilter> logger,
        string toolName,
        int resultLength
    );
}
