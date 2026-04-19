using System.Runtime.CompilerServices;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 将命令行工具声明注入到 ChatOptions 中，使模型可以调用命令行工具
/// </summary>
[ServiceRegister.Scoped]
public class CommandToolMiddleware : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var tools = CommandLineTools.AllToolDeclarations;

        context.Options ??= new ChatOptions();
        context.Options.Tools ??= [];

        foreach (var tool in tools)
            context.Options.Tools!.Add(tool);

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }
}

// [ServiceRegister.Scoped]
// public class SimpleMathToolMiddleware : AgentMiddleware
// {
//     public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
//         ChatResponseUpdateHandler next,
//         [EnumeratorCancellation] CancellationToken ct = default)
//     {
//         var tools = SimpleMathTools.AllToolDeclarations;
//
//         context.Options ??= new ChatOptions();
//         context.Options.Tools ??= [];
//
//         foreach (var tool in tools)
//             context.Options.Tools!.Add(tool);
//
//         await foreach (var update in next().WithCancellation(ct))
//             yield return update;
//     }
// }
