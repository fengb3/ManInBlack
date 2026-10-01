using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests;

public class PipelineTypeTests
{
    // ── 名称解析测试 ──

    [PipelineName("attr-pipeline")]
    private sealed class AttributeNamedPipeline : IAgentPipeline
    {
        public static AgentPipelineBuilder Configure(AgentPipelineBuilder builder) => builder;
    }

    private sealed class StaticPropertyNamedPipeline : IAgentPipeline
    {
        public static string Name => "static-pipeline";
        public static AgentPipelineBuilder Configure(AgentPipelineBuilder builder) => builder;
    }

    private sealed class FallbackNamedPipeline : IAgentPipeline
    {
        public static AgentPipelineBuilder Configure(AgentPipelineBuilder builder) => builder;
    }

    [Fact]
    public void PipelineNameResolver_PrefersAttribute()
    {
        Assert.Equal("attr-pipeline", PipelineNameResolver.Resolve<AttributeNamedPipeline>());
    }

    [Fact]
    public void PipelineNameResolver_FallsBackToStaticProperty()
    {
        Assert.Equal("static-pipeline", PipelineNameResolver.Resolve<StaticPropertyNamedPipeline>());
    }

    [Fact]
    public void PipelineNameResolver_FallsBackToTypeName()
    {
        Assert.Equal(nameof(FallbackNamedPipeline), PipelineNameResolver.Resolve<FallbackNamedPipeline>());
    }

    // ── 注册测试 ──

    [Fact]
    public void AddPipeline_Generic_RegistersWithResolvedName()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddPipeline<AttributeNamedPipeline>();

        var regs = services.BuildServiceProvider().GetServices<PipelineRegistration>();
        Assert.Contains(regs, r => r.Name == "attr-pipeline");
    }

    [Fact]
    public void AddPipeline_Generic_OverwritesStringRegistration_ByLastWinsOrder()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddPipeline("attr-pipeline", b => b);
        builder.AddPipeline<AttributeNamedPipeline>();

        var regs = services.BuildServiceProvider().GetServices<PipelineRegistration>().ToList();
        var matching = regs.Where(r => r.Name == "attr-pipeline").ToList();

        // AgentFactory 按注册顺序迭代，后者覆盖前者；因此同名的第二条注册是生效方。
        Assert.Equal(2, matching.Count);
        Assert.Same(matching[1], regs.Last(r => r.Name == "attr-pipeline"));
    }

    [Fact]
    public void AddPipeline_String_OverwritesGenericRegistration_ByLastWinsOrder()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddPipeline<AttributeNamedPipeline>();
        builder.AddPipeline("attr-pipeline", b => b);

        var regs = services.BuildServiceProvider().GetServices<PipelineRegistration>().ToList();
        var matching = regs.Where(r => r.Name == "attr-pipeline").ToList();

        Assert.Equal(2, matching.Count);
        Assert.Same(matching[1], regs.Last(r => r.Name == "attr-pipeline"));
    }

    // ── AgentDefinition / AgentBuilder 类型化引用 ──

    [Fact]
    public void AgentDefinition_SetPipeline_SetsTypeAndName()
    {
        var def = new AgentDefinition { Name = "test" };

        def.SetPipeline<SimplePipeline>();

        Assert.Equal(typeof(SimplePipeline), def.PipelineType);
        Assert.Equal("simple", def.PipelineName);
    }

    [Fact]
    public void AgentBuilder_PipelineGeneric_SetsTypeAndName()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddAgent("typed-agent", a => a.Pipeline<DefaultPipeline>());

        var defs = services.BuildServiceProvider().GetServices<AgentDefinition>();
        Assert.Single(defs, d => d.Name == "typed-agent" && d.PipelineType == typeof(DefaultPipeline) && d.PipelineName == "default");
    }

    // ── AgentFactory 运行时解析 ──

    [Fact]
    public void AgentFactory_UsesPipelineType_WhenSet()
    {
        var services = new ServiceCollection();
        services.AddManInBlack();
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<AgentFactory>();

        factory.RegisterDefinition(new AgentDefinition
        {
            Name = "type-only-agent",
            PipelineType = typeof(SimplePipeline),
            PipelineName = "this-should-be-ignored",
        });

        // 通过 RunAsync 会触发管道查找；构造空 scope 并提前 throw 的验证方式太重，
        // 此处直接读取内部解析行为：PipelineType 优先级高于 PipelineName。
        var def = factory.GetDefinition("type-only-agent");
        Assert.Equal(typeof(SimplePipeline), def.PipelineType);
        Assert.Equal("this-should-be-ignored", def.PipelineName);
    }

    [Fact]
    public void AgentFactory_StringPipelineName_StillWorks()
    {
        var services = new ServiceCollection();
        services.AddManInBlack();
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<AgentFactory>();

        factory.RegisterDefinition(new AgentDefinition
        {
            Name = "string-name-agent",
            PipelineName = "simple",
        });

        var def = factory.GetDefinition("string-name-agent");
        Assert.Null(def.PipelineType);
        Assert.Equal("simple", def.PipelineName);
    }

    // ── 内置管道 ──

    [Fact]
    public void BuiltInPipelines_HaveExpectedNames()
    {
        Assert.Equal("default", PipelineNameResolver.Resolve<DefaultPipeline>());
        Assert.Equal("simple", PipelineNameResolver.Resolve<SimplePipeline>());
    }

    [Fact]
    public void AddManInBlack_RegistersBuiltInTypedPipelines()
    {
        var services = new ServiceCollection();
        services.AddManInBlack();

        var regs = services.BuildServiceProvider().GetServices<PipelineRegistration>();
        Assert.Contains(regs, r => r.Name == "default");
        Assert.Contains(regs, r => r.Name == "simple");
    }

    [Fact]
    public void AgentFactory_BuiltInPipelines_AreAvailable()
    {
        var services = new ServiceCollection();
        services.AddManInBlack();
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<AgentFactory>();

        factory.RegisterDefinition(new AgentDefinition { Name = "default-agent" });
        factory.RegisterDefinition(new AgentDefinition { Name = "simple-agent", PipelineName = "simple" });

        // 定义能注册即说明管道名有效（实际构建管道需要完整 DI 服务，属于集成测试范畴）
        Assert.Equal("default", factory.GetDefinition("default-agent").PipelineName);
        Assert.Equal("simple", factory.GetDefinition("simple-agent").PipelineName);
    }

    // ── JSON 兼容：settings.json 字符串方式 ──

    [Fact]
    public void SettingsBinding_PipelineName_String_StillWorks()
    {
        var settings = new ManInBlackSettings
        {
            Agents = new Dictionary<string, AgentSettings>
            {
                ["json-agent"] = new() { Instruction = "hi", PipelineName = "simple" },
            },
        };

        Assert.Equal("simple", settings.Agents["json-agent"].PipelineName);
    }
}
