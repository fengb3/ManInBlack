using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Agent;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Agent;

public class AgentConfigurationLoaderTests
{
    [Fact]
    public void LoadFromConfiguration_NullAgents_DoesNothing()
    {
        var services = new ServiceCollection();
        var settings = new ManInBlackSettings { Agents = null };

        AgentConfigurationLoader.LoadFromConfiguration(services, settings);

        var sp = services.BuildServiceProvider();
        var definitions = sp.GetServices<AgentDefinition>();
        Assert.Empty(definitions);
    }

    [Fact]
    public void LoadFromConfiguration_RegistersAgents()
    {
        var services = new ServiceCollection();
        var settings = new ManInBlackSettings
        {
            Agents = new Dictionary<string, AgentSettings>
            {
                ["worker"] = new()
                {
                    Description = "工作 Agent",
                    Instructions = "执行任务",
                    Pipeline = "Analyst"
                }
            }
        };

        AgentConfigurationLoader.LoadFromConfiguration(services, settings);

        var sp = services.BuildServiceProvider();
        var defs = sp.GetServices<AgentDefinition>();
        Assert.Single(defs);
        var def = defs.First();
        Assert.Equal("worker", def.Name);
        Assert.Equal("工作 Agent", def.Description);
        Assert.Equal("Analyst", def.PipelineName);
    }

    [Fact]
    public void LoadFromConfiguration_WithModelRef_ResolvesModel()
    {
        var services = new ServiceCollection();
        var settings = new ManInBlackSettings
        {
            Agents = new Dictionary<string, AgentSettings>
            {
                ["coder"] = new()
                {
                    Description = "代码专家",
                    Instructions = "写代码",
                    Model = "gpt4"
                }
            },
            Models = new Dictionary<string, ModelChoiceSettings>
            {
                ["gpt4"] = new()
                {
                    Provider = "OpenAI",
                    ApiKey = "sk-test",
                    ModelId = "gpt-4o"
                }
            }
        };

        AgentConfigurationLoader.LoadFromConfiguration(services, settings);

        var sp = services.BuildServiceProvider();
        var def = sp.GetServices<AgentDefinition>().First();
        Assert.NotNull(def.Model);
        Assert.Equal("OpenAI", def.Model!.ProviderName);
        Assert.Equal("sk-test", def.Model.ApiKey);
        Assert.Equal("gpt-4o", def.Model.ModelId);
    }

    [Fact]
    public void LoadFromConfiguration_WithEmptyPipeline_FallsBackToSimple()
    {
        // 空管道名称时，AgentBuilder 默认使用 "Simple"
        var services = new ServiceCollection();
        var settings = new ManInBlackSettings
        {
            Agents = new Dictionary<string, AgentSettings>
            {
                ["agent1"] = new()
                {
                    Description = "desc",
                    Instructions = "inst",
                    Pipeline = null
                }
            }
        };

        AgentConfigurationLoader.LoadFromConfiguration(services, settings);

        var sp = services.BuildServiceProvider();
        var def = sp.GetServices<AgentDefinition>().First();
        Assert.Equal("Simple", def.PipelineName);
    }

    [Fact]
    public void LoadFromConfiguration_WithUnknownModel_SkipsModel()
    {
        var services = new ServiceCollection();
        var settings = new ManInBlackSettings
        {
            Agents = new Dictionary<string, AgentSettings>
            {
                ["agent2"] = new()
                {
                    Description = "desc",
                    Instructions = "inst",
                    Model = "nonexistent-model"
                }
            },
            Models = new Dictionary<string, ModelChoiceSettings>()
        };

        AgentConfigurationLoader.LoadFromConfiguration(services, settings);

        var sp = services.BuildServiceProvider();
        var def = sp.GetServices<AgentDefinition>().First();
        Assert.Null(def.Model);
    }
}
