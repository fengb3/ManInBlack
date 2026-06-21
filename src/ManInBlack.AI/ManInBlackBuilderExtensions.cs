using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI;

/// <summary>
/// IManInBlackBuilder 的流式配置扩展方法。
/// </summary>
public static class ManInBlackBuilderExtensions
{
    /// <summary>
    /// 以委托形式添加 Provider 配置。
    /// </summary>
    public static IManInBlackBuilder AddProvider(this IManInBlackBuilder builder, string name, Action<ProviderBuilder> configure)
    {
        var b = new ProviderBuilder();
        configure(b);
        return builder.AddProvider(name, b.Settings);
    }

    /// <summary>
    /// 以对象形式添加 Provider 配置。
    /// </summary>
    public static IManInBlackBuilder AddProvider(this IManInBlackBuilder builder, string name, ProviderSettings provider)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.Providers[name] = provider));
        return builder;
    }

    /// <summary>
    /// 以委托形式添加 ModelChoice 配置。
    /// </summary>
    public static IManInBlackBuilder AddModelChoice(this IManInBlackBuilder builder, string name, Action<ModelChoiceBuilder> configure)
    {
        var b = new ModelChoiceBuilder();
        configure(b);
        return builder.AddModelChoice(name, b.Settings);
    }

    /// <summary>
    /// 以对象形式添加 ModelChoice 配置。
    /// </summary>
    public static IManInBlackBuilder AddModelChoice(this IManInBlackBuilder builder, string name, ModelChoiceSettings choice)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.ModelChoices[name] = choice));
        return builder;
    }

    /// <summary>
    /// 以委托形式添加 Agent 配置，构建并注册 AgentDefinition 单例。
    /// </summary>
    public static IManInBlackBuilder AddAgent(this IManInBlackBuilder builder, string name, Action<AgentBuilder> configure)
    {
        var a = new AgentBuilder(name);
        configure(a);
        return builder.AddAgent(a.Build());
    }

    /// <summary>
    /// 以对象形式添加 Agent 配置，注册 AgentDefinition 单例并写入 settings 供校验。
    /// </summary>
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

    /// <summary>
    /// 注册命名管道，即时注册 PipelineRegistration 单例供 AgentFactory 收集。
    /// </summary>
    public static IManInBlackBuilder AddPipeline(this IManInBlackBuilder builder, string name, Func<AgentPipelineBuilder, AgentPipelineBuilder> resolver)
    {
        // 即时注册 PipelineRegistration 单例（AgentFactory ctor 收集）
        ((ManInBlackBuilder)builder).Services.AddSingleton(new PipelineRegistration(name, resolver));
        return builder;
    }

    /// <summary>
    /// 添加 Hook 配置，按添加顺序累积。
    /// </summary>
    public static IManInBlackBuilder AddHook(this IManInBlackBuilder builder, Action<HookBuilder> configure)
    {
        var h = new HookBuilder();
        configure(h);
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.Hooks.Add(h.Settings)));
        return builder;
    }

    /// <summary>
    /// 以委托形式添加 MCP Server 配置。
    /// </summary>
    public static IManInBlackBuilder AddMcpServer(this IManInBlackBuilder builder, string name, Action<McpServerBuilder> configure)
    {
        var m = new McpServerBuilder();
        configure(m);
        return builder.AddMcpServer(name, m.Settings);
    }

    /// <summary>
    /// 以对象形式添加 MCP Server 配置。
    /// </summary>
    public static IManInBlackBuilder AddMcpServer(this IManInBlackBuilder builder, string name, McpServerSettings server)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.McpServers[name] = server));
        return builder;
    }

    /// <summary>
    /// 配置存储设置（根路径、工作空间等）。
    /// </summary>
    public static IManInBlackBuilder UseStorage(this IManInBlackBuilder builder, Action<StorageBuilder> configure)
    {
        var s = new StorageBuilder();
        configure(s);
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(settings => settings.Storage = s.Settings));
        return builder;
    }

    /// <summary>
    /// 启用 bubblewrap 沙盒执行模式。
    /// </summary>
    public static IManInBlackBuilder UseSandbox(this IManInBlackBuilder builder)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.UseSandbox = true));
        return builder;
    }

    /// <summary>
    /// 载入 ~/.man-in-black/settings.json 作为配置源（缺失则创建默认）。
    /// 位置决定合并层：放链首则后续委托覆盖 JSON 同名 key。
    /// </summary>
    public static IManInBlackBuilder UseJson(this IManInBlackBuilder builder)
    {
        var loaded = ManInBlackConfigurationBuilder.LoadSettings();
        return ApplySource(builder, loaded);
    }

    /// <summary>
    /// 复用已有 IConfiguration（Web 场景）作为配置源。同时绑定 FeishuSettings 供适配器读取。
    /// </summary>
    public static IManInBlackBuilder UseConfiguration(this IManInBlackBuilder builder, IConfiguration configuration)
    {
        var loaded = new ManInBlackSettings();
        configuration.Bind(loaded);
        // Configure 仅注册 IConfigureOptions，延迟到 IOptions<T> 首次访问才解析，与下方 ApplySource 顺序无关。
        builder.Services.Configure<FeishuSettings>(configuration.GetSection("Feishu"));
        return ApplySource(builder, loaded);
    }

    /// <summary>
    /// 将配置源应用到 builder：即时注册 AgentDefinition 单例，并贡献按 key 合并。
    /// </summary>
    private static IManInBlackBuilder ApplySource(IManInBlackBuilder builder, ManInBlackSettings source)
    {
        var concrete = (ManInBlackBuilder)builder;
        // A：即时注册每个 agent 的 AgentDefinition 单例
        foreach (var (name, agent) in source.Agents)
        {
            concrete.Services.AddSingleton(new AgentDefinition
            {
                Name = name,
                Description = agent.Description,
                Instruction = agent.Instruction,
                PipelineName = agent.PipelineName,
                SubAgents = agent.SubAgents,
                ModelChoiceName = agent.ModelChoiceName,
            });
        }
        // B：贡献合并文件/IConfiguration 内容进 settings
        concrete.AddContribution(new ActionContribution(s => SettingsMerger.Merge(s, source)));
        return builder;
    }
}
