using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tools;

public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, IToolDeclaration> _declarations;

    public ToolRegistry(IEnumerable<IToolDeclaration> declarations)
    {
        // 同名工具首个注册者胜出：容忍"手动 AddToolHandlers + AddManInBlack 自动应用"等重复注册场景
        _declarations = new(declarations
            .GroupBy(d => d.ToolName)
            .ToDictionary(g => g.Key, g => g.First()));
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
