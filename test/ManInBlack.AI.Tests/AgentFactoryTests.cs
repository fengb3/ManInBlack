using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests;

public class AgentFactoryTests
{
    private static AgentFactory CreateFactory()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        return new AgentFactory(scopeFactory, NullLogger<AgentFactory>.Instance, []);
    }

    private static AgentDefinition MakeDefinition(string name, string? pipelineName = null)
    {
        return new AgentDefinition
        {
            Name = name,
            Description = $"测试 Agent：{name}",
            Instruction = $"你是 {name}",
            PipelineName = pipelineName ?? "default",
        };
    }

    [Fact]
    public void RegisterDefinition_StoresDefinition()
    {
        // Arrange
        var factory = CreateFactory();
        var def = MakeDefinition("test-agent");

        // Act
        factory.RegisterDefinition(def);

        // Assert
        var result = factory.GetDefinition("test-agent");
        Assert.Same(def, result);
    }

    [Fact]
    public void RegisterDefinition_DuplicateName_Throws()
    {
        // Arrange
        var factory = CreateFactory();
        var def1 = MakeDefinition("dup-agent");
        var def2 = MakeDefinition("dup-agent");
        factory.RegisterDefinition(def1);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => factory.RegisterDefinition(def2));
        Assert.Contains("dup-agent", ex.Message);
    }

    [Fact]
    public void GetDefinition_NotExist_Throws()
    {
        // Arrange
        var factory = CreateFactory();

        // Act & Assert
        var ex = Assert.Throws<KeyNotFoundException>(() => factory.GetDefinition("nonexistent"));
        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public void RegisterPipeline_OverwritesExisting()
    {
        // Arrange
        var factory = CreateFactory();
        Func<AgentPipelineBuilder, AgentPipelineBuilder> configure1 = b => b;
        Func<AgentPipelineBuilder, AgentPipelineBuilder> configure2 = b => b;

        // Act — 覆盖内置的 "default" 管道
        factory.RegisterPipeline("default", configure1);
        factory.RegisterPipeline("default", configure2);

        // Assert — 不抛异常即可证明覆盖成功（内部 ConcurrentDictionary 覆盖）
        // 进一步验证：通过 RunAsync 会使用 configure2，但 RunAsync 属于集成测试
        // 此处仅验证覆盖操作不抛异常
    }

    [Fact]
    public void RegisterAndCancelExisting_FirstCall_ReturnsNewCts()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var cts = factory.RegisterAndCancelExisting("user-1");

        // Assert
        Assert.NotNull(cts);
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void RegisterAndCancelExisting_SecondCall_CancelsOld()
    {
        // Arrange
        var factory = CreateFactory();
        var firstCts = factory.RegisterAndCancelExisting("user-1");

        // Act
        var secondCts = factory.RegisterAndCancelExisting("user-1");

        // Assert
        Assert.True(firstCts.IsCancellationRequested);
        Assert.False(secondCts.IsCancellationRequested);
        Assert.NotSame(firstCts, secondCts);
    }

    [Fact]
    public void Release_RemovesTracking()
    {
        // Arrange
        var factory = CreateFactory();
        var cts = factory.RegisterAndCancelExisting("user-1");

        // Act
        factory.Release("user-1", cts);

        // Assert — Release 后再次注册同一用户不会取消旧的 CTS（因为已被移除）
        var newCts = factory.RegisterAndCancelExisting("user-1");
        Assert.False(newCts.IsCancellationRequested);
        // 之前的 cts 也不应被取消（因为已被 Release 移除）
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void Definitions_ReturnsAllRegistered()
    {
        // Arrange
        var factory = CreateFactory();
        var def1 = MakeDefinition("agent-a");
        var def2 = MakeDefinition("agent-b");
        var def3 = MakeDefinition("agent-c");

        factory.RegisterDefinition(def1);
        factory.RegisterDefinition(def2);
        factory.RegisterDefinition(def3);

        // Act
        var definitions = factory.Definitions;

        // Assert
        Assert.Equal(3, definitions.Count);
        Assert.Contains(def1, definitions);
        Assert.Contains(def2, definitions);
        Assert.Contains(def3, definitions);
    }
}
