using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace ManInBlack.AI.Middlewares;

[ServiceRegister.Scoped]
public partial class ToolsMiddleware(ToolRegistry registry) : AgentMiddleware
{
    private readonly ToolRegistry _registry = registry;
    private readonly string[]? _groups;

    public ToolsMiddleware(ToolRegistry registry, string[] groups) : this(registry)
    {
        _groups = groups;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context, ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        context.Options ??= new ChatOptions();
        context.Options.Tools ??= [];

        var declarations = _groups is null
            ? _registry.GetAll()
            : _registry.GetByGroups(_groups);

        foreach (var d in declarations)
            context.Options.Tools!.Add(d);

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }
}
