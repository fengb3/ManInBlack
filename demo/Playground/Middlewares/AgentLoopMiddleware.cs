using System.Text;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Core.Tools;
using Microsoft.Extensions.AI;

namespace Playground.Middlewares;

/// <summary>
/// Agent 循环中间件，自动处理模型返回的 tool call 并将结果追加到消息历史
/// </summary>
[ServiceRegister.Scoped]
public class AgentLoopMiddleware(IToolExecutor toolExecutor) : AgentMiddleware
{

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        CancellationToken ct = default)
    {
        while (true)
        {
            var functionCalls = new List<FunctionCallContent>();
            var textBuffer = new StringBuilder();

            await foreach (var update in next().WithCancellation(ct))
            {
                // 收集 tool call
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent fcc)
                        functionCalls.Add(fcc);
                }

                yield return update;
            }

            // 没有 tool call → 循环结束
            if (functionCalls.Count == 0)
                yield break;

            // 追加 assistant 消息（包含 tool calls）
            context.Messages.Add(new ChatMessage(ChatRole.Assistant, functionCalls.Cast<AIContent>().ToList()));

            // 执行每个 tool call，追加结果消息
            var toolResults = new List<AIContent>();
            foreach (var fc in functionCalls)
            {
                var toolCtx = new ToolExecuteContext(context.ServiceProvider)
                {
                    ToolName = fc.Name,
                    CallId = fc.CallId,
                    Arguments = fc.Arguments as Dictionary<string, object?> ?? new Dictionary<string, object?>()
                };

                await toolExecutor.ExecuteAsync(toolCtx, ct);

                toolResults.Add(new FunctionResultContent(fc.CallId, toolCtx.Error?.Message ?? toolCtx.Result));
            }

            context.Messages.Add(new ChatMessage(ChatRole.Tool, toolResults));
        }
    }
}
