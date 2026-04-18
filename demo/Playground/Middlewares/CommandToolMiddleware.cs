using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
// using Playground.Tools;

namespace Playground.Middlewares;

public class CommandToolMiddleware(ILogger<CommandToolMiddleware> logger, IServiceProvider sp) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next, CancellationToken ct = default)
    {
        // var tools = CommandLineTools.AllToolDeclarations;
        //
        // context.Options       ??= new ChatOptions();
        // context.Options.Tools ??= [];
        //
        // foreach (var tool in tools)
        //     context.Options.Tools!.Add(tool);

        await foreach (var update in next().WithCancellation(ct))
        {
            yield return update;
        }
    }
}