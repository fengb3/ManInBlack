using ManInBlack.AI;
using ManInBlack.AI.Abstraction.Factory;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Factory;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Factory;

public class DependencyInjectionTests
{
    [Fact]
    public void AddAgent_RegistersFactoryAndPreset()
    {
        var services = new ServiceCollection();
        services.AddManInBlack(opt =>
        {
            opt.ModelChoice = new ModelChoice 
            { 
                Provider = new OpenAIProvider(), 
                ModelId = "test" 
            };
        });
        services.AddAgent("test-agent", preset => preset
            .WithName("测试")
            .WithInstruction("系统提示"));

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<AgentFactory>();
        Assert.NotNull(factory);

        // Verify the preset dictionary was also registered
        var dict = sp.GetRequiredService<IDictionary<string, AgentPreset>>();
        Assert.NotNull(dict);
    }

    [Fact]
    public void AddAgent_MultiplePresets_AllResolvable()
    {
        var services = new ServiceCollection();
        services.AddManInBlack(opt =>
        {
            opt.ModelChoice = new ModelChoice 
            { 
                Provider = new OpenAIProvider(), 
                ModelId = "test" 
            };
        });
        services.AddAgent("agent-a", p => p.WithName("A").WithInstruction("指令A"));
        services.AddAgent("agent-b", p => p.WithName("B").WithInstruction("指令B"));

        var sp = services.BuildServiceProvider();
        var dict = sp.GetRequiredService<IDictionary<string, AgentPreset>>();

        Assert.Equal(2, dict.Count);
        Assert.Equal("指令A", dict["agent-a"].Instruction);
        Assert.Equal("指令B", dict["agent-b"].Instruction);
    }

    [Fact]
    public void AddAgent_SameNameOverwrites_LastWins()
    {
        var services = new ServiceCollection();
        services.AddManInBlack(opt =>
        {
            opt.ModelChoice = new ModelChoice 
            { 
                Provider = new OpenAIProvider(), 
                ModelId = "test" 
            };
        });
        services.AddAgent("agent", p => p.WithName("V1").WithInstruction("版本1"));
        services.AddAgent("agent", p => p.WithName("V2").WithInstruction("版本2"));

        var sp = services.BuildServiceProvider();
        var dict = sp.GetRequiredService<IDictionary<string, AgentPreset>>();

        Assert.Single(dict);
        Assert.Equal("版本2", dict["agent"].Instruction);
    }

    [Fact]
    public void AddAgent_CreatesFactoryWhenFirstAgentAdded()
    {
        var services = new ServiceCollection();
        services.AddManInBlack(opt =>
        {
            opt.ModelChoice = new ModelChoice 
            { 
                Provider = new OpenAIProvider(), 
                ModelId = "test" 
            };
        });

        // After adding agent, AgentFactory should be registered
        services.AddAgent("test-agent", p => p.WithName("测试").WithInstruction("系统提示"));
        
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<AgentFactory>();
        Assert.NotNull(factory);
        
        var dict = sp.GetRequiredService<IDictionary<string, AgentPreset>>();
        Assert.NotNull(dict);
    }

    [Fact]
    public void AddAgent_PresetPreservesAllProperties()
    {
        var services = new ServiceCollection();
        services.AddManInBlack(opt =>
        {
            opt.ModelChoice = new ModelChoice 
            { 
                Provider = new OpenAIProvider(), 
                ModelId = "test" 
            };
        });
        
        services.AddAgent("complete-agent", p => p
            .WithName("完整预设")
            .WithDescription("一个完整的预设测试")
            .WithInstruction("完整的系统提示")
            .UsePipeline(AgentPipelineNames.Simple));

        var sp = services.BuildServiceProvider();
        var dict = sp.GetRequiredService<IDictionary<string, AgentPreset>>();
        var preset = dict["complete-agent"];

        Assert.Equal("完整预设", preset.Name);
        Assert.Equal("一个完整的预设测试", preset.Description);
        Assert.Equal("完整的系统提示", preset.Instruction);
        Assert.Equal("Simple", preset.PipelineName);
    }
}