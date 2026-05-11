using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests;

public class AgentSettingsTests
{
    private static ManInBlackSettings CreateValidSettings(params (string Name, AgentSettings Settings)[] agents)
    {
        var settings = new ManInBlackSettings
        {
            Providers = new Dictionary<string, ProviderSettings>
            {
                ["default"] = new() { Schema = "OpenAI", ApiKey = "sk-test" },
                ["deepseek"] = new() { Schema = "OpenAI", ApiKey = "sk-deep", BaseUrl = "https://api.deepseek.com" },
            },
            ModelChoices = new Dictionary<string, ModelChoiceSettings>
            {
                ["default"] = new() { ProviderName = "default", ModelId = "gpt-4o" },
                ["cheap"] = new() { ProviderName = "deepseek", ModelId = "deepseek-chat" },
            },
        };

        foreach (var (name, agent) in agents)
            settings.Agents[name] = agent;

        return settings;
    }

    // ── 校验测试 ──

    [Fact]
    public void Validate_EmptyAgents_Passes()
    {
        var settings = CreateValidSettings();
        var validator = new ValidateManInBlackSettings();

        var result = validator.Validate(null, settings);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_ValidAgents_Passes()
    {
        var settings = CreateValidSettings(
            ("translator", new AgentSettings
            {
                Description = "翻译专家",
                Instruction = "你是翻译专家",
                PipelineName = "sub-agent",
                ModelChoiceName = "cheap",
            }),
            ("main-agent", new AgentSettings
            {
                Instruction = "你是助手",
                PipelineName = "default",
                SubAgents = ["translator"],
            })
        );
        var validator = new ValidateManInBlackSettings();

        var result = validator.Validate(null, settings);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_EmptyPipelineName_Fails()
    {
        var settings = CreateValidSettings(
            ("bad-agent", new AgentSettings { PipelineName = "" })
        );
        var validator = new ValidateManInBlackSettings();

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("PipelineName", result.FailureMessage);
    }

    [Fact]
    public void Validate_SelfReferencingSubAgent_Fails()
    {
        var settings = CreateValidSettings(
            ("loop-agent", new AgentSettings { SubAgents = ["loop-agent"] })
        );
        var validator = new ValidateManInBlackSettings();

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("不能将自己列为子 Agent", result.FailureMessage);
    }

    [Fact]
    public void Validate_NonExistentSubAgent_Fails()
    {
        var settings = CreateValidSettings(
            ("main-agent", new AgentSettings { SubAgents = ["ghost-agent"] })
        );
        var validator = new ValidateManInBlackSettings();

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("ghost-agent", result.FailureMessage);
    }

    [Fact]
    public void Validate_InvalidModelChoiceName_Fails()
    {
        var settings = CreateValidSettings(
            ("agent", new AgentSettings { ModelChoiceName = "nonexistent" })
        );
        var validator = new ValidateManInBlackSettings();

        var result = validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("nonexistent", result.FailureMessage);
    }

    [Fact]
    public void Validate_NullModelChoiceName_Passes()
    {
        var settings = CreateValidSettings(
            ("agent", new AgentSettings { ModelChoiceName = null })
        );
        var validator = new ValidateManInBlackSettings();

        var result = validator.Validate(null, settings);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    // ── AgentSettings → AgentDefinition 映射测试 ──

    [Fact]
    public void AgentSettings_MapsToDefinition()
    {
        var agentSettings = new AgentSettings
        {
            Description = "翻译专家",
            Instruction = "你是翻译专家",
            PipelineName = "sub-agent",
            SubAgents = ["researcher"],
            ModelChoiceName = "cheap",
        };

        var definition = new AgentDefinition
        {
            Name = "translator",
            Description = agentSettings.Description,
            Instruction = agentSettings.Instruction,
            PipelineName = agentSettings.PipelineName,
            SubAgents = agentSettings.SubAgents,
            ModelChoiceName = agentSettings.ModelChoiceName,
        };

        Assert.Equal("translator", definition.Name);
        Assert.Equal("翻译专家", definition.Description);
        Assert.Equal("你是翻译专家", definition.Instruction);
        Assert.Equal("sub-agent", definition.PipelineName);
        Assert.Equal(["researcher"], definition.SubAgents);
        Assert.Equal("cheap", definition.ModelChoiceName);
    }

    // ── DI 自动注册测试 ──

    [Fact]
    public void AgentsFromSettings_AreRegisteredAsDefinitions()
    {
        var settings = new ManInBlackSettings
        {
            Providers = new Dictionary<string, ProviderSettings>
            {
                ["default"] = new() { Schema = "OpenAI", ApiKey = "sk-test" },
            },
            ModelChoices = new Dictionary<string, ModelChoiceSettings>
            {
                ["default"] = new() { ProviderName = "default", ModelId = "gpt-4o" },
            },
            Agents = new Dictionary<string, AgentSettings>
            {
                ["agent-a"] = new() { Instruction = "Agent A", PipelineName = "default" },
                ["agent-b"] = new() { Instruction = "Agent B", PipelineName = "simple" },
            },
        };

        // 模拟 AddManInBlackFromConfiguration 中的注册逻辑
        var services = new ServiceCollection();
        foreach (var (agentName, agentSettings) in settings.Agents)
        {
            services.AddSingleton(new AgentDefinition
            {
                Name = agentName,
                Description = agentSettings.Description,
                Instruction = agentSettings.Instruction,
                PipelineName = agentSettings.PipelineName,
                SubAgents = agentSettings.SubAgents,
                ModelChoiceName = agentSettings.ModelChoiceName,
            });
        }

        var sp = services.BuildServiceProvider();
        var definitions = sp.GetServices<AgentDefinition>().ToList();

        Assert.Equal(2, definitions.Count);
        Assert.Contains(definitions, d => d.Name == "agent-a" && d.Instruction == "Agent A");
        Assert.Contains(definitions, d => d.Name == "agent-b" && d.Instruction == "Agent B" && d.PipelineName == "simple");
    }

    [Fact]
    public void AgentSettings_DefaultValues()
    {
        var settings = new AgentSettings();

        Assert.Equal(string.Empty, settings.Description);
        Assert.Equal(string.Empty, settings.Instruction);
        Assert.Equal("default", settings.PipelineName);
        Assert.Empty(settings.SubAgents);
        Assert.Null(settings.ModelChoiceName);
    }

    [Fact]
    public void AgentDefinition_HasModelChoiceName()
    {
        var def = new AgentDefinition
        {
            Name = "test",
            ModelChoiceName = "cheap",
        };

        Assert.Equal("cheap", def.ModelChoiceName);
    }

    [Fact]
    public void AgentDefinition_ModelChoiceName_DefaultsNull()
    {
        var def = new AgentDefinition { Name = "test" };

        Assert.Null(def.ModelChoiceName);
    }
}
