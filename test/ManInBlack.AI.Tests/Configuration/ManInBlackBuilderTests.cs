using ManInBlack.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class ManInBlackBuilderTests
{
    /// <summary>
    /// 从已注册的 IManInBlackContribution 直接合并出 ManInBlackSettings，跳过完整 DI。
    /// </summary>
    internal static ManInBlackSettings Merge(IServiceCollection services)
    {
        var contributions = services.BuildServiceProvider().GetServices<IManInBlackContribution>();
        var settings = new ManInBlackSettings();
        new ManInBlackSettingsBuilder(contributions).Configure(settings);
        return settings;
    }

    [Fact]
    public void Merge_Dict_LastWriteWinsByKey()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Providers["a"] = new ProviderSettings { Schema = "OpenAI", ApiKey = "old" }));
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Providers["a"] = new ProviderSettings { Schema = "OpenAI", ApiKey = "new" }));
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Providers["b"] = new ProviderSettings { Schema = "Anthropic", ApiKey = "kb" }));

        var settings = Merge(services);

        Assert.Equal("new", settings.Providers["a"].ApiKey);
        Assert.Equal("Anthropic", settings.Providers["b"].Schema);
        Assert.Equal(2, settings.Providers.Count);
    }

    [Fact]
    public void Merge_Hooks_Accumulate()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Hooks.Add(new HookSettings())));
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Hooks.Add(new HookSettings())));

        var settings = Merge(services);

        Assert.Equal(2, settings.Hooks.Count);
    }

    [Fact]
    public void Merge_Scalar_LastWriteWins()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.UseSandbox = false));
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.UseSandbox = true));

        var settings = Merge(services);

        Assert.True(settings.UseSandbox);
    }

    [Fact]
    public void SettingsMerger_FullSource_MergesByKey()
    {
        var target = new ManInBlackSettings();
        target.Providers["existing"] = new ProviderSettings { Schema = "OpenAI", ApiKey = "keep" };

        var source = new ManInBlackSettings();
        source.Providers["existing"] = new ProviderSettings { Schema = "OpenAI", ApiKey = "override" };
        source.Providers["new"] = new ProviderSettings { Schema = "Gemini", ApiKey = "kn" };
        source.UseSandbox = true;

        SettingsMerger.Merge(target, source);

        Assert.Equal("override", target.Providers["existing"].ApiKey);
        Assert.True(target.Providers.ContainsKey("new"));
        Assert.True(target.UseSandbox);
    }

    [Fact]
    public void AddProvider_Delegate_WritesProvider()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx").BaseUrl("https://api.deepseek.com"));

        var settings = Merge(services);

        Assert.Equal("OpenAI", settings.Providers["default"].Schema);
        Assert.Equal("sk-xxx", settings.Providers["default"].ApiKey);
        Assert.Equal("https://api.deepseek.com", settings.Providers["default"].BaseUrl);
    }

    [Fact]
    public void AddProvider_ObjectOverload_WritesProvider()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddProvider("default", new ProviderSettings { Schema = "Anthropic", ApiKey = "k" });

        var settings = Merge(services);

        Assert.Equal("Anthropic", settings.Providers["default"].Schema);
    }

    [Fact]
    public void AddModelChoice_Delegate_WritesChoice()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddModelChoice("default", c => c.Provider("default").ModelId("gpt-4o"));

        var settings = Merge(services);

        Assert.Equal("default", settings.ModelChoices["default"].ProviderName);
        Assert.Equal("gpt-4o", settings.ModelChoices["default"].ModelId);
    }
}
