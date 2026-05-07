using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Agent;
using Xunit;

namespace ManInBlack.AI.Tests.Agent;

public class AgentBuilderTests
{
    [Fact]
    public void Build_ReturnsCorrectDefinition()
    {
        var model = new AgentModelOptions { ProviderName = "OpenAI", ModelId = "gpt-4" };

        var def = new AgentBuilder("my-agent")
            .WithDescription("测试描述")
            .WithInstructions("执行任务")
            .WithPipeline("Coder")
            .WithModel(model)
            .Build();

        Assert.Equal("my-agent", def.Name);
        Assert.Equal("测试描述", def.Description);
        Assert.Equal("执行任务", def.Instructions);
        Assert.Equal("Coder", def.PipelineName);
        Assert.NotNull(def.Model);
        Assert.Equal("OpenAI", def.Model!.ProviderName);
        Assert.Equal("gpt-4", def.Model.ModelId);
    }

    [Fact]
    public void WithPipeline_SetsPipelineName()
    {
        var def = new AgentBuilder("tool-agent")
            .WithPipeline("Shell")
            .Build();

        Assert.Equal("Shell", def.PipelineName);
    }

    [Fact]
    public void WithModel_SetsModel()
    {
        var model = new AgentModelOptions
        {
            ProviderName = "Anthropic",
            ApiKey = "key-123",
            BaseUrl = "https://api.anthropic.com",
            ModelId = "claude-3"
        };

        var def = new AgentBuilder("m-agent")
            .WithModel(model)
            .Build();

        Assert.NotNull(def.Model);
        Assert.Equal("Anthropic", def.Model!.ProviderName);
        Assert.Equal("key-123", def.Model.ApiKey);
        Assert.Equal("https://api.anthropic.com", def.Model.BaseUrl);
        Assert.Equal("claude-3", def.Model.ModelId);
    }

    [Fact]
    public void Build_WithOnlyName_ReturnsMinimalDefinition()
    {
        var def = new AgentBuilder("minimal").Build();

        Assert.Equal("minimal", def.Name);
        Assert.Equal(string.Empty, def.Description);
        Assert.Equal(string.Empty, def.Instructions);
        Assert.Equal("Simple", def.PipelineName);
        Assert.Null(def.Model);
    }
}
