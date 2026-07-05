using ManInBlack.AI;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class ToolExtraParameterConfigTests
{
    // 校验器要求至少一个 Provider+ModelChoice,这里给最小 seed 让 IOptions 能 resolve
    private static IManInBlackBuilder BuildWithDefaults(ServiceCollection services) =>
        services.AddManInBlack()
            .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-x"))
            .AddModelChoice("default", c => c.Provider("default").ModelId("gpt-4o"));

    [Fact]
    public void AddToolExtraParameter_AfterUseConfiguration_CodeValueWins()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "sk-x",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
            ["ToolExtraParameter:ParamName"] = "from-json"
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        BuildWithDefaults(services)
            .UseConfiguration(cfg)
            .AddToolExtraParameter(p => p.ParamName("purpose").Required(true));

        var settings = services.BuildServiceProvider()
            .GetRequiredService<IOptions<ManInBlackSettings>>().Value;

        Assert.Equal("purpose", settings.ToolExtraParameter.ParamName);
        Assert.True(settings.ToolExtraParameter.Required);
    }

    [Fact]
    public void UseConfiguration_BindsToolExtraParameterSection_FromJson()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "sk-x",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
            ["ToolExtraParameter:ParamName"] = "from-json",
            ["ToolExtraParameter:ParamDescription"] = "desc-from-json",
            ["ToolExtraParameter:Required"] = "true"
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        BuildWithDefaults(services).UseConfiguration(cfg);

        var settings = services.BuildServiceProvider()
            .GetRequiredService<IOptions<ManInBlackSettings>>().Value;

        Assert.Equal("from-json", settings.ToolExtraParameter.ParamName);
        Assert.Equal("desc-from-json", settings.ToolExtraParameter.ParamDescription);
        Assert.True(settings.ToolExtraParameter.Required);
    }
}
