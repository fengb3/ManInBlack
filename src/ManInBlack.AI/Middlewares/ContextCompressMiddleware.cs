using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

[ServiceRegister.Scoped]
public class ContextCompressMiddleware : AgentMiddleware
{

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 对上下文中的tool结果进行压缩, 保留 近10个 Tool 结果, 旧的用 [old tool result has been compressed] 代替

        int keeped  = 10;
        int transed = 0;
        int compressed = 0;

        // 反着遍历
        for (var i = context.Messages.Count - 1; i >= 0; i--)
        {
            var message = context.Messages[i];
            for (var j = message.Contents.Count - 1; j >= 0; j--)
            {
                if (message.Contents[j] is FunctionResultContent functionResultContent)
                {
                    if (transed < keeped)
                    {
                        transed++;
                        continue;
                    }
                    compressed++;
                    const string replaceContent = "[old tool result content cleared]";
                    if((functionResultContent.Result?.ToString()?.Length ?? 0) > replaceContent.Length)
                        message.Contents[j] = new FunctionResultContent(functionResultContent.CallId, new TextContent(replaceContent));
                }
            }

        }
        
        // Console.BackgroundColor = ConsoleColor.DarkBlue;
        // Console.ForegroundColor = ConsoleColor.Cyan;
        // Console.WriteLine($"ContextCompressMiddleware: compressed {compressed} tool results, keeped {transed} tool results.");
        // Console.ResetColor();


        await foreach (var response in next().WithCancellation(ct)) yield return response;
    }
}