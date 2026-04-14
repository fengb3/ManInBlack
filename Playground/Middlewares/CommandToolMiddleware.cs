using ManInBlack.AI.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Playground.Tools;

namespace Playground.Middlewares;

public class CommandToolMiddleware(ILogger<CommandToolMiddleware> logger, IServiceProvider sp) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context, Func<IAsyncEnumerable<ChatResponseUpdate>> next, CancellationToken cancellationToken = default)
    {
        var tools = CommandLineTools.AllToolDeclarations;

        context.Options       ??= new ChatOptions();
        context.Options.Tools ??= [];

        foreach (var tool in tools)
            context.Options.Tools!.Add(tool);

        await foreach (var update in next().WithCancellation(cancellationToken))
        {
            yield return update;
        }
    }
}