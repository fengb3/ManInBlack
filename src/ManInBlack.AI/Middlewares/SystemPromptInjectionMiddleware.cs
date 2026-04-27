using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 注入系统提示词中间件, 系统提示次构建的其他中间件 需要再此中间件之前执行，在消息列表开头插入系统提示消息
/// </summary>
[ServiceRegister.Scoped]
public class SystemPromptInjectionMiddleware(ILogger<SystemPromptInjectionMiddleware> logger) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        
        // Console.ForegroundColor = ConsoleColor.DarkGreen;
        // Console.BackgroundColor = ConsoleColor.Magenta;
        // Console.WriteLine("add system prompt to context: " + context.SystemPrompt);
        // Console.ResetColor();
        
        logger.LogInformation("Inject system prompt with length of {Length} to context", context.SystemPrompt.Length);
        context.Messages.Insert(0, new ChatMessage(ChatRole.System, context.SystemPrompt));

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }
}

[ServiceRegister.Scoped]
public class UserInputMiddleware() : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Console.ForegroundColor = ConsoleColor.DarkGreen;
        // Console.BackgroundColor = ConsoleColor.Magenta;
        // Console.WriteLine("add user input to context: " + context.UserInput);
        // Console.ResetColor();
        
        context.Messages.Add(new ChatMessage(ChatRole.User, context.UserInput));

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }
}