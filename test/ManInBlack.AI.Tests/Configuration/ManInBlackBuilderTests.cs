using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.Configuration;
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

    [Fact]
    public void AddAgent_Delegate_RegistersDefinitionAndWritesSettings()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddAgent("console-agent", a => a
            .Instruction("你是AI助手")
            .Pipeline("default")
            .SubAgents("sub"));

        var settings = Merge(services);

        // settings.Agents 供校验
        Assert.Equal("你是AI助手", settings.Agents["console-agent"].Instruction);
        Assert.Contains("sub", settings.Agents["console-agent"].SubAgents);
        // AgentDefinition 即时注册为单例
        var defs = services.BuildServiceProvider().GetServices<AgentDefinition>();
        Assert.Single(defs, d => d.Name == "console-agent" && d.PipelineName == "default");
    }

    [Fact]
    public void AddPipeline_RegistersPipelineRegistrationSingleton()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddPipeline("custom", b => b.UseSimple());

        var regs = services.BuildServiceProvider().GetServices<PipelineRegistration>();
        Assert.Single(regs, r => r.Name == "custom");
    }

    [Fact]
    public void AddHook_Accumulates()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddHook(h => h.HookPoint("before_run").Run("echo a"));
        builder.AddHook(h => h.HookPoint("after_run").Run("echo b"));

        var settings = Merge(services);

        Assert.Equal(2, settings.Hooks.Count);
        Assert.Equal("before_run", settings.Hooks[0].HookPoint);
        Assert.Equal("echo b", settings.Hooks[1].Script);
    }

    [Fact]
    public void AddMcpServer_Delegate_WritesServer()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddMcpServer("tavily", m => m.Endpoint("https://mcp.tavily.com/mcp").Header("Authorization", "Bearer xxx"));

        var settings = Merge(services);

        Assert.Equal("https://mcp.tavily.com/mcp", settings.McpServers["tavily"].Endpoint);
        Assert.Equal("Bearer xxx", settings.McpServers["tavily"].Headers!["Authorization"]);
    }

    [Fact]
    public void UseStorage_Delegate_WritesStorage()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.UseStorage(s => s.RootPath("/data/mib").Workspace(w => w.Mode(WorkspaceMode.CustomPath).CustomPath("/ws")));

        var settings = Merge(services);

        Assert.Equal("/data/mib", settings.Storage!.RootPath);
        Assert.Equal(WorkspaceMode.CustomPath, settings.Storage!.Workspace!.Mode);
        Assert.Equal("/ws", settings.Storage!.Workspace!.CustomPath);
    }

    [Fact]
    public void UseSandbox_SetsFlag()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.UseSandbox();

        var settings = Merge(services);

        Assert.True(settings.UseSandbox);
    }

    [Fact]
    public void UseConfiguration_BindsAndMergesAndBindsFeishu()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "from-cfg",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
            ["Agents:console-agent:Instruction"] = "cfg agent",
            ["Agents:console-agent:PipelineName"] = "default",
            ["Feishu:AppId"] = "cli_xxx",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);
        builder.UseConfiguration(configuration);

        var settings = Merge(services);

        Assert.Equal("from-cfg", settings.Providers["default"].ApiKey);
        Assert.Equal("cfg agent", settings.Agents["console-agent"].Instruction);
        // Feishu 单独绑定
        var feishu = services.BuildServiceProvider().GetRequiredService<IOptions<FeishuSettings>>().Value;
        Assert.Equal("cli_xxx", feishu.AppId);
        // 每个 agent 即时注册 AgentDefinition 单例
        Assert.Single(services.BuildServiceProvider().GetServices<AgentDefinition>(), d => d.Name == "console-agent");
    }

    // UseJson 与 UseConfiguration 共用私有 ApplySource（合并 + 即时注册 AgentDefinition），
    // 此处用 UseConfiguration 模拟源以避免触碰真实文件系统；
    // UseJson 的文件读取路径（LoadSettings）属既有行为，不在本单测覆盖。
    [Fact]
    public void UseConfiguration_ThenAddProvider_DelegateOverridesSourceByKey()
    {
        // 用 UseConfiguration 模拟 JSON 源，再追加委托覆盖，验证 last-write-wins。
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "from-json",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);
        builder.UseConfiguration(configuration);       // 模拟 JSON 源（链首）
        builder.AddProvider("default", p => p.Schema("OpenAI").ApiKey("from-delegate")); // 覆盖

        var settings = Merge(services);

        Assert.Equal("from-delegate", settings.Providers["default"].ApiKey);
    }
}
