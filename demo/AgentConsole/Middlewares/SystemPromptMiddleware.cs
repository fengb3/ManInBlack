using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middleware;

/// <summary>
/// 系统提示词中间件，在消息列表开头插入系统提示消息
/// </summary>
public class SystemPromptMiddleware(string systemPrompt) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        Func<IAsyncEnumerable<ChatResponseUpdate>> next, 
        CancellationToken cancellationToken = default)
    {
        context.Messages.Insert(0, new ChatMessage(ChatRole.System, systemPrompt));

        await foreach (var update in next().WithCancellation(cancellationToken))
            yield return update;
    }
}