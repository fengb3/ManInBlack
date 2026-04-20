using System.Runtime.CompilerServices;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 将文件操作工具声明注入到 ChatOptions 中，使模型可以读写文件和搜索内容
/// </summary>
[ServiceRegister.Scoped]
public class FileToolMiddleware : AgentMiddleware
{
    public override IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var tools = FileTools.AllToolDeclarations;

        context.Options ??= new ChatOptions();
        context.Options.Tools ??= [];

        foreach (var tool in tools)
            context.Options.Tools!.Add(tool);

        // await foreach (var update in next().WithCancellation(ct))
        //     yield return update;

        return next();
    }
}
