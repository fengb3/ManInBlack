using System.Runtime.CompilerServices;
using System.Text;
using ManInBlack.AI.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;
using AgentConsole;

namespace AgentConsole.Middlewares;

/// <summary>
/// Agent 循环中间件，自动处理模型返回的 tool call 并将结果追加到消息历史
/// </summary>
public class AgentLoopMiddleware(ToolExecutor toolExecutor) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        Func<IAsyncEnumerable<ChatResponseUpdate>> next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var functionCalls = new List<FunctionCallContent>();

            await foreach (var update in next().WithCancellation(cancellationToken))
            {
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent fcc)
                        functionCalls.Add(fcc);
                }

                yield return update;
            }

            if (functionCalls.Count == 0)
                yield break;

            // 追加 assistant 消息（包含 tool calls）
            context.Messages.Add(new ChatMessage(ChatRole.Assistant, functionCalls.Cast<AIContent>().ToList()));

            // 执行每个 tool call
            var toolResults = new List<AIContent>();
            foreach (var fc in functionCalls)
            {
                var toolCtx = new ToolExecuteContext(context.ServiceProvider)
                {
                    ToolName = fc.Name,
                    CallId = fc.CallId,
                    Arguments = fc.Arguments as Dictionary<string, object?> ?? new Dictionary<string, object?>()
                };

                await toolExecutor.ExecuteAsync(toolCtx, cancellationToken);
                toolResults.Add(new FunctionResultContent(fc.CallId, toolCtx.Error?.Message ?? toolCtx.Result));
            }

            context.Messages.Add(new ChatMessage(ChatRole.Tool, toolResults));
        }
    }
}
