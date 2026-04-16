using System.Runtime.CompilerServices;
using System.Text;
using ManInBlack.AI.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;
using AgentConsole;
using ManInBlack.AI.Attributes;

namespace AgentConsole.Middlewares;

/// <summary>
/// Agent 循环中间件，自动处理模型返回的 tool call 并将结果追加到消息历史
/// </summary>
[ServiceRegister.Scoped]
public class AgentLoopMiddleware(IToolExecutor toolExecutor) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        Func<IAsyncEnumerable<ChatResponseUpdate>> next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var functionCalls = new List<FunctionCallContent>();
            var textBuilder = new StringBuilder();
            var reasoningBuilder = new StringBuilder();

            await foreach (var update in next().WithCancellation(cancellationToken))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent fcc:
                            functionCalls.Add(fcc);
                            break;
                        case TextContent text:
                            textBuilder.Append(text.Text);
                            break;
                        case TextReasoningContent reasoning:
                            reasoningBuilder.Append(reasoning.Text);
                            break;
                    }
                }

                yield return update;
            }

            // 构建 assistant 消息内容（text + reasoning + function calls）
            var assistantContents = new List<AIContent>();
            if (reasoningBuilder.Length > 0)
                assistantContents.Add(new TextReasoningContent(reasoningBuilder.ToString()));
            if (textBuilder.Length > 0)
                assistantContents.Add(new TextContent(textBuilder.ToString()));
            assistantContents.AddRange(functionCalls);

            if (assistantContents.Count > 0)
                context.Messages.Add(new ChatMessage(ChatRole.Assistant, assistantContents));

            if (functionCalls.Count == 0)
                yield break;

            // 执行每个 tool call 并将结果通过流式输出
            var toolResults = new List<AIContent>();
            foreach (var fc in functionCalls)
            {
                var toolCtx = new ToolExecuteContext(context.ServiceProvider)
                {
                    ToolName = fc.Name,
                    CallId = fc.CallId,
                    Arguments = fc.Arguments
                };

                await toolExecutor.ExecuteAsync(toolCtx, cancellationToken);

                // if (toolCtx.Error != null)
                // {
                //     Console.BackgroundColor = ConsoleColor.Red;
                //     Console.ForegroundColor = ConsoleColor.White;
                //     Console.WriteLine($"Error: {toolCtx.Error}");
                //     Console.ResetColor();
                // }
                
                var result = new FunctionResultContent(fc.CallId, toolCtx.Error?.Message ?? toolCtx.Result);
                toolResults.Add(result);
                yield return new ChatResponseUpdate(ChatRole.Tool, [result]);
            }

            context.Messages.Add(new ChatMessage(ChatRole.Tool, toolResults));
        }
    }
}
