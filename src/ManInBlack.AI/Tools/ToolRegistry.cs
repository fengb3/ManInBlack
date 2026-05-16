using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tools;

public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, IToolDeclaration> _declarations;

    public ToolRegistry(IEnumerable<IToolDeclaration> declarations)
    {
        _declarations = new(declarations.ToDictionary(d => d.ToolName));
    }

    public IReadOnlyList<AIFunctionDeclaration> GetAll()
        => _declarations.Values.Select(d => d.Declaration).ToList();

    public IReadOnlyList<AIFunctionDeclaration> GetByGroups(params string[] groups)
        => _declarations.Values
            .Where(d => groups.Contains(d.Group))
            .Select(d => d.Declaration)
            .ToList();

    public void Register(IToolDeclaration declaration)
        => _declarations[declaration.ToolName] = declaration;
}
