using ManInBlack.AI.Configuration;

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
}
