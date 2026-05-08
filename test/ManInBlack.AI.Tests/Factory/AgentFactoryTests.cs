using ManInBlack.AI.Abstraction.Factory;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Factory;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Factory;

public class AgentFactoryTests
{
    /// <summary>
    /// 创建一个包含测试预设的字典
    /// </summary>
    private static Dictionary<string, AgentPreset> CreateTestPresets()
    {
        return new Dictionary<string, AgentPreset>
        {
            ["assistant"] = new()
            {
                Name = "测试助手",
                Instruction = "你是一个测试助手",
                PipelineName = AgentPipelineNames.Default
            },
            ["simple-bot"] = new()
            {
                Name = "简单机器人",
                Description = "一个简单的机器人",
                Instruction = "你是简单机器人",
                PipelineName = AgentPipelineNames.Simple
            }
        };
    }

    [Fact]
    public void Create_WithRegisteredPreset_ReturnsAgentInstance()
    {
        // Arrange
        var presets = CreateTestPresets();
        var sp = TestHelpers.EmptyServiceProvider;
        var factory = new AgentFactory(sp, presets);
        var options = new AgentCreateOptions { UserInput = "你好" };

        // Act
        var agent = factory.Create("assistant", options);

        // Assert
        Assert.NotNull(agent);
        Assert.NotNull(agent.Context);
        Assert.NotNull(agent.Pipeline);
    }

    [Fact]
    public void Create_SetsSystemPromptFromPresetInstruction()
    {
        var presets = CreateTestPresets();
        var sp = TestHelpers.EmptyServiceProvider;
        var factory = new AgentFactory(sp, presets);
        var options = new AgentCreateOptions { UserInput = "你好" };

        var agent = factory.Create("assistant", options);

        Assert.Equal("你是一个测试助手", agent.Context.SystemPrompt);
    }

    [Fact]
    public void Create_SetsUserInputFromOptions()
    {
        var presets = CreateTestPresets();
        var sp = TestHelpers.EmptyServiceProvider;
        var factory = new AgentFactory(sp, presets);
        var options = new AgentCreateOptions { UserInput = "帮我写代码" };

        var agent = factory.Create("assistant", options);

        Assert.Equal("帮我写代码", agent.Context.UserInput);
    }

    [Fact]
    public void Create_GeneratesUniqueAgentId()
    {
        var presets = CreateTestPresets();
        var sp = TestHelpers.EmptyServiceProvider;
        var factory = new AgentFactory(sp, presets);
        var options = new AgentCreateOptions { UserInput = "你好" };

        var agent = factory.Create("assistant", options);

        Assert.False(string.IsNullOrEmpty(agent.Context.AgentId));
    }

    [Fact]
    public void Create_WithUnknownPreset_ThrowsKeyNotFoundException()
    {
        var presets = CreateTestPresets();
        var sp = TestHelpers.EmptyServiceProvider;
        var factory = new AgentFactory(sp, presets);
        var options = new AgentCreateOptions { UserInput = "你好" };

        Assert.Throws<KeyNotFoundException>(() => factory.Create("nonexistent", options));
    }

    [Fact]
    public void Create_Twice_ReturnsDifferentAgentContextInstances()
    {
        var presets = CreateTestPresets();
        var sp = TestHelpers.EmptyServiceProvider;
        var factory = new AgentFactory(sp, presets);
        var options = new AgentCreateOptions { UserInput = "你好" };

        var agent1 = factory.Create("assistant", options);
        var agent2 = factory.Create("assistant", options);

        Assert.NotSame(agent1.Context, agent2.Context);
    }

    [Fact]
    public void Create_SetsRuntimePropertiesFromOptions()
    {
        var presets = CreateTestPresets();
        var sp = TestHelpers.EmptyServiceProvider;
        var factory = new AgentFactory(sp, presets);
        var options = new AgentCreateOptions
        {
            UserInput = "测试",
            ParentId = "test-user",
            ParentType = "User",
            SessionId = "session-123"
        };

        var agent = factory.Create("assistant", options);

        Assert.Equal("test-user", agent.Context.ParentId);
        Assert.Equal("User", agent.Context.ParentType);
        Assert.Equal("session-123", agent.Context.SessionId);
    }
}