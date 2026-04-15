using AgentConsole.Tools;
using ManInBlack.AI.Middleware;
using Microsoft.Extensions.AI;

namespace AgentConsole.Middlewares;

/// <summary>
/// 将命令行工具声明注入到 ChatOptions 中，使模型可以调用命令行工具
/// </summary>
public class CommandToolMiddleware : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        Func<IAsyncEnumerable<ChatResponseUpdate>> next,
        CancellationToken cancellationToken = default)
    {
        var tools = CommandLineTools.AllToolDeclarations;

        context.Options       ??= new ChatOptions();
        context.Options.Tools ??= [];

        foreach (var tool in tools)
            context.Options.Tools!.Add(tool);

        await foreach (var update in next().WithCancellation(cancellationToken))
            yield return update;
    }
}
