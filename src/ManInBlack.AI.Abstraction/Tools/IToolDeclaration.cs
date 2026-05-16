using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Tools;

public interface IToolDeclaration
{
    string ToolName { get; }
    string Group { get; }
    AIFunctionDeclaration Declaration { get; }
}
