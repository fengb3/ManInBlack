using ManInBlack.AI.Attributes;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middleware;

/// <summary>
/// 系统提示词中间件，在消息列表开头插入系统提示消息
/// </summary>
[ServiceRegister.Scoped]
public class SystemPromptMiddleware() : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        Func<IAsyncEnumerable<ChatResponseUpdate>> next,
        CancellationToken cancellationToken = default)
    {
        
        // Console.ForegroundColor = ConsoleColor.DarkGreen;
        // Console.BackgroundColor = ConsoleColor.Magenta;
        // Console.WriteLine("add system prompt to context: " + context.SystemPrompt);
        // Console.ResetColor();
        
        context.Messages.Insert(0, new ChatMessage(ChatRole.System, context.SystemPrompt));

        await foreach (var update in next().WithCancellation(cancellationToken))
            yield return update;
    }
}

[ServiceRegister.Scoped]
public class UserInputMiddleware() : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        Func<IAsyncEnumerable<ChatResponseUpdate>> next,
        CancellationToken cancellationToken = default)
    {
        // Console.ForegroundColor = ConsoleColor.DarkGreen;
        // Console.BackgroundColor = ConsoleColor.Magenta;
        // Console.WriteLine("add user input to context: " + context.UserInput);
        // Console.ResetColor();
        
        context.Messages.Add(new ChatMessage(ChatRole.User, context.UserInput));

        await foreach (var update in next().WithCancellation(cancellationToken))
            yield return update;
    }
}