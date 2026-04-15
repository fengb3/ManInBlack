using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middleware;

/// <summary>
/// 系统提示词中间件，在消息列表开头插入系统提示消息
/// </summary>
public class SystemPromptMiddleware : AgentMiddleware
{
    private readonly string _systemPrompt;

    public SystemPromptMiddleware(string systemPrompt)
    {
        _systemPrompt = systemPrompt;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        Func<IAsyncEnumerable<ChatResponseUpdate>> next,
        CancellationToken cancellationToken = default)
    {
        context.Messages.Insert(0, new ChatMessage(ChatRole.System, _systemPrompt));

        await foreach (var update in next().WithCancellation(cancellationToken))
            yield return update;
    }
}
