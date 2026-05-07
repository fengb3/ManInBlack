using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Agent;
using Xunit;

namespace ManInBlack.AI.Tests.Agent;

public class BuiltInAgentsTests
{
    [Fact]
    public void Coder_ReturnsValidDefinition()
    {
        var def = BuiltInAgents.Coder();

        Assert.Equal("coder", def.Name);
        Assert.NotEmpty(def.Description);
        Assert.NotEmpty(def.Instructions);
        Assert.Equal("Coder", def.PipelineName);
    }

    [Fact]
    public void Shell_ReturnsValidDefinition()
    {
        var def = BuiltInAgents.Shell();

        Assert.Equal("shell", def.Name);
        Assert.NotEmpty(def.Description);
        Assert.NotEmpty(def.Instructions);
        Assert.Equal("Shell", def.PipelineName);
    }

    [Fact]
    public void Analyst_ReturnsValidDefinition()
    {
        var def = BuiltInAgents.Analyst();

        Assert.Equal("analyst", def.Name);
        Assert.NotEmpty(def.Description);
        Assert.NotEmpty(def.Instructions);
        Assert.Equal("Analyst", def.PipelineName);
    }

    [Fact]
    public void Coder_WithModel_SetsModel()
    {
        var model = new AgentModelOptions
        {
            ProviderName = "OpenAI",
            ModelId = "gpt-4o",
            ApiKey = "test-key"
        };

        var def = BuiltInAgents.Coder(model);

        Assert.Equal("coder", def.Name);
        Assert.NotNull(def.Model);
        Assert.Equal("OpenAI", def.Model!.ProviderName);
        Assert.Equal("gpt-4o", def.Model.ModelId);
        Assert.Equal("test-key", def.Model.ApiKey);
    }

    [Fact]
    public void General_ReturnsValidDefinition()
    {
        var def = BuiltInAgents.General();

        Assert.Equal("general", def.Name);
        Assert.NotEmpty(def.Description);
        Assert.NotEmpty(def.Instructions);
        Assert.Equal("Default", def.PipelineName);
    }

    [Fact]
    public void General_WithModel_SetsModel()
    {
        var model = new AgentModelOptions
        {
            ProviderName = "Anthropic",
            ModelId = "claude-3",
            ApiKey = "sk-ant-test"
        };

        var def = BuiltInAgents.General(model);

        Assert.Equal("general", def.Name);
        Assert.Equal("Default", def.PipelineName);
        Assert.NotNull(def.Model);
        Assert.Equal("Anthropic", def.Model!.ProviderName);
        Assert.Equal("claude-3", def.Model.ModelId);
        Assert.Equal("sk-ant-test", def.Model.ApiKey);
    }
}
