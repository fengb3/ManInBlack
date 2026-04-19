using System.Runtime.CompilerServices;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
// using Playground.Tools;

namespace Playground.Middlewares;
#pragma warning disable CS9113 // Parameter is unread
public class CommandToolMiddleware(ILogger<CommandToolMiddleware> _logger, IServiceProvider _sp) : AgentMiddleware
#pragma warning restore CS9113 // Parameter is unread
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next, [EnumeratorCancellation] CancellationToken ct = default)
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