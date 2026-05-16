using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tools;

public sealed class ToolDeclaration(string toolName, string group, AIFunctionDeclaration declaration)
    : IToolDeclaration
{
    public string ToolName => toolName;
    public string Group => group;
    public AIFunctionDeclaration Declaration => declaration;
}
