using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI;

/// <summary>
/// IManInBlackBuilder 的流式配置扩展方法。
/// </summary>
public static class ManInBlackBuilderExtensions
{
    public static IManInBlackBuilder AddProvider(this IManInBlackBuilder builder, string name, Action<ProviderBuilder> configure)
    {
        var b = new ProviderBuilder();
        configure(b);
        return builder.AddProvider(name, b.Settings);
    }

    public static IManInBlackBuilder AddProvider(this IManInBlackBuilder builder, string name, ProviderSettings provider)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.Providers[name] = provider));
        return builder;
    }

    public static IManInBlackBuilder AddModelChoice(this IManInBlackBuilder builder, string name, Action<ModelChoiceBuilder> configure)
    {
        var b = new ModelChoiceBuilder();
        configure(b);
        return builder.AddModelChoice(name, b.Settings);
    }

    public static IManInBlackBuilder AddModelChoice(this IManInBlackBuilder builder, string name, ModelChoiceSettings choice)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.ModelChoices[name] = choice));
        return builder;
    }

    public static IManInBlackBuilder AddAgent(this IManInBlackBuilder builder, string name, Action<AgentBuilder> configure)
    {
        var a = new AgentBuilder(name);
        configure(a);
        return builder.AddAgent(a.Build());
    }

    public static IManInBlackBuilder AddAgent(this IManInBlackBuilder builder, AgentDefinition definition)
    {
        var concrete = (ManInBlackBuilder)builder;
        // A：即时注册 AgentDefinition 单例（AgentFactory ctor 收集）
        concrete.Services.AddSingleton(definition);
        // B：贡献写入 settings.Agents，供 ValidateManInBlackSettings 校验
        concrete.AddContribution(new ActionContribution(s =>
        {
            s.Agents[definition.Name] = new AgentSettings
            {
                Description = definition.Description,
                Instruction = definition.Instruction,
                PipelineName = definition.PipelineName,
                SubAgents = definition.SubAgents,
                ModelChoiceName = definition.ModelChoiceName,
            };
        }));
        return builder;
    }

    public static IManInBlackBuilder AddPipeline(this IManInBlackBuilder builder, string name, Func<AgentPipelineBuilder, AgentPipelineBuilder> resolver)
    {
        // 即时注册 PipelineRegistration 单例（AgentFactory ctor 收集）
        ((ManInBlackBuilder)builder).Services.AddSingleton(new PipelineRegistration(name, resolver));
        return builder;
    }
}
