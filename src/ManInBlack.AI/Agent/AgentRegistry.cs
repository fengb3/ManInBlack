using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Abstraction.Attributes;

namespace ManInBlack.AI.Agent;

/// <summary>
/// Agent 注册表，管理 Agent 定义的注册与查找
/// </summary>
[ServiceRegister.Singleton]
public class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentDefinition> _agents = new();

    /// <summary>
    /// 创建 AgentRegistry 并自动注册通过 DI 注入的所有 AgentDefinition
    /// </summary>
    public AgentRegistry(IEnumerable<AgentDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            Register(definition);
        }
    }

    /// <inheritdoc />
    public void Register(AgentDefinition definition)
    {
        _agents.AddOrUpdate(definition.Name, _ => definition, (_, _) => definition);
    }

    /// <inheritdoc />
    public AgentDefinition? Get(string name)
    {
        return _agents.TryGetValue(name, out var definition) ? definition : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<AgentDefinition> GetAll()
    {
        return _agents.Values.ToList().AsReadOnly();
    }
}
