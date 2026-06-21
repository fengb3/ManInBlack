using ManInBlack.AI;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class AddManInBlackEndToEndTests
{
    [Fact]
    public void AddManInBlack_PureDelegate_ResolvesSettingsAndDefinition()
    {
        var services = new ServiceCollection();
        services.AddManInBlack()
            .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx"))
            .AddModelChoice("default", c => c.Provider("default").ModelId("gpt-4o"))
            .AddAgent("a1", a => a.Instruction("hi").Pipeline("simple"));

        var sp = services.BuildServiceProvider();

        var settings = sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value;
        Assert.Equal("sk-xxx", settings.Providers["default"].ApiKey);

        var factory = sp.GetRequiredService<AgentFactory>();
        Assert.Equal("a1", factory.GetDefinition("a1").Name);
    }

    [Fact]
    public void AddManInBlack_DelegateOverriddenFromJsonPath_WorksViaUseConfiguration()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "from-json",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
        };
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        services.AddManInBlack()
            .UseConfiguration(cfg)
            .AddProvider("default", p => p.Schema("OpenAI").ApiKey("from-delegate"));

        var settings = services.BuildServiceProvider().GetRequiredService<IOptions<ManInBlackSettings>>().Value;
        Assert.Equal("from-delegate", settings.Providers["default"].ApiKey);
    }

    [Fact]
    public void AddManInBlack_ValidationFails_WhenNoProvider()
    {
        var services = new ServiceCollection();
        services.AddManInBlack().UseSandbox();

        var sp = services.BuildServiceProvider();
        // 解析 IOptions<ManInBlackSettings>.Value 触发校验
        var ex = Assert.Throws<OptionsValidationException>(() =>
            sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value);
        Assert.Contains("Providers", ex.Message);
    }
}
