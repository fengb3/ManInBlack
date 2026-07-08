using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Mcp;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ManInBlack.AI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI;

public class ManInBlackOptions
{
    public ModelChoice ModelChoice { get; set; } = default!;
    public AgentStorageOptions Storage { get; set; } = new();

    /// <summary>
    /// 是否启用 Linux 下的 bubblewrap 沙盒执行命令。默认 false。
    /// </summary>
    public bool UseSandbox { get; set; }
}

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// 注册 ManInBlack 全部核心服务，返回流式 builder。
        /// 默认不读取任何文件；需 JSON 时链式调用 .UseJson()，需复用 IConfiguration 时调用 .UseConfiguration(cfg)。
        /// </summary>
        public IManInBlackBuilder AddManInBlack()
        {
            // 合并基础设施：贡献 → IConfigureOptions<ManInBlackSettings>，再走现有校验器
            services.AddOptions();
            services.AddSingleton<IConfigureOptions<ManInBlackSettings>>(
                sp => new ManInBlackSettingsBuilder(sp.GetServices<IManInBlackContribution>()));
            services.AddSingleton<IValidateOptions<ManInBlackSettings>, ValidateManInBlackSettings>();

            // AgentStorageOptions：从合并后的 settings.Storage 映射（resolve 期工厂）
            services.AddSingleton<IConfigureOptions<AgentStorageOptions>, AgentStorageOptionsConfigurer>();

            // 默认 ModelChoice 单例：从合并后的 settings 解析
            services.AddSingleton<ModelChoice>(sp =>
                sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value.GetDefaultModelChoice());

            // SQLite 持久化:连接串从 RootPath 取
            services.AddDbContextFactory<ManInBlackDbContext>((sp, o) =>
            {
                var root = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.RootPath;
                Directory.CreateDirectory(root);
                o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")}");
                o.AddInterceptors(new SqliteInitInterceptor());
            });

            services.AddScoped<AgentPipelineBuilder>();
            services.AddScoped<AgentContext>();
            services.AddSingleton<AgentFactory>();

            // LLM IChatClient 专用命名 HttpClient(ManInBlackHttpClients.ChatClient):
            // - 移除 host(AddServiceDefaults)注入的默认标准 resilience(每次尝试 30s 超时 + 自动重试)。
            //   它会砍断推理模型首字节>30s 的流式请求(TimeoutRejectedException),且其重试会与应用层
            //   RetryMiddleware 叠加,放大延迟与计费。LLM 的重试已由 RetryMiddleware 统一负责。
            // - 30 分钟兜底超时:防极端静默挂死;正常流式时长由应用层 CancellationToken 控制。
            //   (HttpClient.Timeout 会覆盖整条流式生命周期,故需远大于 Polly 默认的 30s。)
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers 为评估期 API,语义稳定
            services.AddHttpClient(ManInBlackHttpClients.ChatClient, c => c.Timeout = TimeSpan.FromMinutes(30))
                .RemoveAllResilienceHandlers()
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
#pragma warning restore EXTEXP0001

            services.AddScoped<IChatClient>(sp =>
            {
                var choice = sp.GetRequiredService<ModelChoice>();
                return ChatClientProviderExtensions.CreateChatClient(
                    sp.GetRequiredService<IHttpClientFactory>(), choice);
            });

            services.TryAddSingleton<IAgentStateStorage>(
                sp => (IAgentStateStorage)sp.GetRequiredService<ISessionStorage>());
            services.TryAddSingleton<ICheckpointPolicy, AfterToolCallPolicy>();

            services.AddScoped<IUserWorkspace>(sp =>
            {
                var ws = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.Workspace;
                return ws.Mode switch
                {
                    WorkspaceMode.CurrentDirectory => new CurrentDirectoryWorkspace(),
                    WorkspaceMode.CustomPath => new CustomPathWorkspace(
                        sp.GetRequiredService<IOptions<AgentStorageOptions>>()),
                    _ => new FileUserWorkspace(
                        sp.GetRequiredService<IOptions<AgentStorageOptions>>(),
                        sp.GetRequiredService<AgentContext>(),
                        sp.GetRequiredService<IUserStorage>())
                };
            });

            services.AddScoped<FileAccessPolicyResolver>();

            services.AddAutoRegisteredServices();

            // 沙盒:UseSandbox 在 IOptions resolve 时才确定,故做成 resolve 期工厂
            services.AddScoped<IShellExecutor>(sp =>
            {
                var useSandbox = sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value.UseSandbox;
                if (OperatingSystem.IsLinux() && useSandbox)
                {
                    var policy = sp.GetRequiredService<FileAccessPolicyResolver>().Resolve();
                    return new BwarpShellExecutor(policy);
                }
                return new ProcessShellExecutor();
            });
            services.AddToolHandlers();

            // MCP：单例 client 池（HostedService 启动时连接 server + 注册工具声明）+ 工具执行 provider
            services.AddSingleton<McpClientHostedService>();
            services.AddSingleton<IMcpToolProvider, McpToolProvider>();
            services.AddHostedService(sp => sp.GetRequiredService<McpClientHostedService>());

            return new ManInBlackBuilder(services);
        }

        /// <summary>
        /// 从 ~/.man-in-black/settings.json 加载配置并注册所有服务（旧入口，等价于 AddManInBlack().UseJson()）。
        /// </summary>
        public IServiceCollection AddManInBlackFromSettings(Action<ManInBlackOptions>? configure = null)
        {
            var builder = services.AddManInBlack().UseJson();
            ApplyLegacyOptions(builder, configure);
            return services;
        }

        /// <summary>
        /// 从给定 IConfiguration 加载配置并注册所有服务（旧入口，等价于 AddManInBlack().UseConfiguration(cfg)）。
        /// 适用于已构建 WebApplicationBuilder 等场景，可复用其 Configuration。
        /// </summary>
        public IServiceCollection AddManInBlackFromConfiguration(
            IConfiguration configuration,
            Action<ManInBlackOptions>? configure = null)
        {
            var builder = services.AddManInBlack().UseConfiguration(configuration);
            ApplyLegacyOptions(builder, configure);
            return services;
        }

        /// <summary>
        /// [Obsolete] 旧的窄委托入口。改用 services.AddManInBlack().AddProvider/... 流式 API。
        /// </summary>
        [Obsolete("改用 services.AddManInBlack().AddProvider(...).AddModelChoice(...) 流式 API")]
        public IServiceCollection AddManInBlack(Action<ManInBlackOptions> configure)
        {
            // 复用与 FromSettings/FromConfiguration 相同的旧选项映射逻辑（见 ApplyLegacyOptions），避免重复
            ApplyLegacyOptions(services.AddManInBlack(), configure);
            return services;
        }

        /// <summary>
        /// 注册 Agent 定义到 DI 容器。AgentFactory 构造时会自动收集并注册所有定义。
        /// </summary>
        public IServiceCollection AddAgentDefinition(AgentDefinition definition)
        {
            services.AddSingleton(definition);
            return services;
        }
    }

    /// <summary>
    /// 把旧入口的 <see cref="Action{ManInBlackOptions}"/> 透传委托映射到流式 builder。
    /// 多数场景 <paramref name="configure"/> 为 null，此时直接返回（已由 .UseJson()/.UseConfiguration() 完成配置）。
    /// </summary>
    private static void ApplyLegacyOptions(IManInBlackBuilder builder, Action<ManInBlackOptions>? configure)
    {
        if (configure is null) return;
        var options = new ManInBlackOptions();
        configure(options);
        builder.AddProvider("default", p => p
            .Schema(options.ModelChoice.Schema)
            .ApiKey(options.ModelChoice.ApiKey)
            .BaseUrl(string.IsNullOrEmpty(options.ModelChoice.BaseUrl) ? null : options.ModelChoice.BaseUrl));
        builder.AddModelChoice("default", c => c.Provider("default").ModelId(options.ModelChoice.ModelId));
        if (options.Storage.RootPath is not null || options.Storage.Workspace is not null)
            builder.UseStorage(s =>
            {
                if (options.Storage.RootPath is not null) s.RootPath(options.Storage.RootPath);
                if (options.Storage.Workspace is not null) s.Workspace(w => w.Mode(options.Storage.Workspace.Mode).CustomPath(options.Storage.Workspace.CustomPath ?? ""));
            });
        if (options.UseSandbox)
            builder.UseSandbox();
    }
}
