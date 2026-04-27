using System.Runtime.CompilerServices;
using System.Text;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// Agent 循环中间件，自动处理模型返回的 tool call 并将结果追加到消息历史
/// </summary>
[ServiceRegister.Scoped]
public class AgentLoopMiddleware(IToolExecutor toolExecutor, ILogger<AgentContext> logger) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (true)
        {
            var functionCalls = new List<FunctionCallContent>();
            var textBuilder = new StringBuilder();
            var reasoningBuilder = new StringBuilder();

            await foreach (var update in next().WithCancellation(ct))
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
                    case UsageContent usageContent:
                        context.AccumulatedUsage.Add(usageContent.Details);
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

                await toolExecutor.ExecuteAsync(toolCtx, ct);

                if (toolCtx.Error != null)
                {
                    Console.BackgroundColor = ConsoleColor.Red;
                    Console.ForegroundColor = ConsoleColor.White;
                    // Console.WriteLine($"Error: {toolCtx.Error}");
                    logger.LogError(toolCtx.Error, "Error executing tool {ToolName} in agent {AgentId}", toolCtx.ToolName, context.AgentId);
                    Console.ResetColor();
                }
                
                var result = new FunctionResultContent(fc.CallId, toolCtx.Error?.Message ?? toolCtx.Result);
                toolResults.Add(result);
                yield return new ChatResponseUpdate(ChatRole.Tool, [result]);
            }

            context.Messages.Add(new ChatMessage(ChatRole.Tool, toolResults));
        }
    }
}
