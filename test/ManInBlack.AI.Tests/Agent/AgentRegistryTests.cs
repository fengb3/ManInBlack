using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Agent;
using Xunit;

namespace ManInBlack.AI.Tests.Agent;

public class AgentRegistryTests
{
    private static AgentDefinition MakeDef(string name) => new() { Name = name, Description = $"{name} desc" };

    [Fact]
    public void Register_And_Get_ReturnsDefinition()
    {
        var registry = new AgentRegistry([]);
        var def = MakeDef("test-agent");

        registry.Register(def);
        var result = registry.Get("test-agent");

        Assert.NotNull(result);
        Assert.Equal("test-agent", result.Name);
        Assert.Equal("test-agent desc", result.Description);
    }

    [Fact]
    public void Get_UnknownName_ReturnsNull()
    {
        var registry = new AgentRegistry([]);

        var result = registry.Get("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetAll_ReturnsAllRegistered()
    {
        var defs = new[] { MakeDef("a"), MakeDef("b"), MakeDef("c") };
        var registry = new AgentRegistry(defs);

        var all = registry.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Contains(all, d => d.Name == "a");
        Assert.Contains(all, d => d.Name == "b");
        Assert.Contains(all, d => d.Name == "c");
    }

    [Fact]
    public void Register_DuplicateName_Overwrites()
    {
        var registry = new AgentRegistry([]);
        registry.Register(new AgentDefinition { Name = "dup", Description = "v1" });
        registry.Register(new AgentDefinition { Name = "dup", Description = "v2" });

        var result = registry.Get("dup");
        Assert.NotNull(result);
        Assert.Equal("v2", result.Description);

        Assert.Single(registry.GetAll());
    }
}
