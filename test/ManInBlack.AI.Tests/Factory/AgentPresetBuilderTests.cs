using ManInBlack.AI.Abstraction.Factory;
using ManInBlack.AI.Factory;
using Xunit;

namespace ManInBlack.AI.Tests.Factory;

public class AgentPresetBuilderTests
{
    [Fact]
    public void Build_WithRequiredFields_ReturnsCorrectPreset()
    {
        var preset = new AgentPresetBuilder()
            .WithName("测试助手")
            .WithInstruction("你是一个测试助手")
            .Build();

        Assert.Equal("测试助手", preset.Name);
        Assert.Equal("你是一个测试助手", preset.Instruction);
        Assert.Equal(AgentPipelineNames.Default, preset.PipelineName);
    }

    [Fact]
    public void Build_WithAllFields_ReturnsCorrectPreset()
    {
        var preset = new AgentPresetBuilder()
            .WithName("助手")
            .WithDescription("一个 AI 助手")
            .WithInstruction("系统提示词")
            .UsePipeline(AgentPipelineNames.Simple)
            .Build();

        Assert.Equal("助手", preset.Name);
        Assert.Equal("一个 AI 助手", preset.Description);
        Assert.Equal("系统提示词", preset.Instruction);
        Assert.Equal(AgentPipelineNames.Simple, preset.PipelineName);
    }

    [Fact]
    public void Build_WithoutName_ThrowsInvalidOperationException()
    {
        var builder = new AgentPresetBuilder()
            .WithInstruction("系统提示词");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_WithoutInstruction_ThrowsInvalidOperationException()
    {
        var builder = new AgentPresetBuilder()
            .WithName("助手");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_DefaultPipelineName_IsDefault()
    {
        var preset = new AgentPresetBuilder()
            .WithName("助手")
            .WithInstruction("提示词")
            .Build();

        Assert.Equal("Default", preset.PipelineName);
    }
}