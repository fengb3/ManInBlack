using System.Text;
using ManInBlack.AI.Middleware;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI;

/// <summary>
/// 可执行的 Agent，封装管道调用和消息管理
/// </summary>
public class Agent
{
    private readonly Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> _pipeline;
    private readonly IServiceProvider _serviceProvider;

    public Agent(
        Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>> pipeline,
        IServiceProvider serviceProvider)
    {
        _pipeline = pipeline;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 流式执行，返回 ChatResponseUpdate 流
    /// </summary>
    public IAsyncEnumerable<ChatResponseUpdate> RunAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var context = new AgentContext(_serviceProvider)
        {
            Messages = [new ChatMessage(ChatRole.User, prompt)],
            IsStreaming = true,
            CancellationToken = cancellationToken
        };

        return _pipeline(context);
    }

    /// <summary>
    /// 执行到结束，返回结构化结果
    /// </summary>
    public async Task<AgentResult> RunToEndAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var context = new AgentContext(_serviceProvider)
        {
            Messages = [new ChatMessage(ChatRole.User, prompt)],
            IsStreaming = true,
            CancellationToken = cancellationToken
        };

        var text = new StringBuilder();
        var steps = 0;

        await foreach (var update in _pipeline(context).WithCancellation(cancellationToken))
        {
            steps++;
            foreach (var content in update.Contents)
            {
                if (content is TextContent tc)
                    text.Append(tc.Text);
            }
        }

        return new AgentResult
        {
            Text = text.ToString(),
            Steps = steps,
            Messages = context.Messages
        };
    }
}
