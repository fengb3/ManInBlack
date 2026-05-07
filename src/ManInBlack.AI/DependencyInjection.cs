using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Agent;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI;

public class ManInBlackOptions
{
    public ModelChoice ModelChoice { get; set; } = default!;
    public AgentStorageOptions Storage { get; set; } = new();
}

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// 注册 ManInBlack 核心服务、基础中间件和工具
        /// </summary>
        public IServiceCollection AddManInBlack(Action<ManInBlackOptions> configure)
        {
            var options = new ManInBlackOptions();
            configure(options);

            services.Configure<AgentStorageOptions>(opt =>
            {
                opt.RootPath = options.Storage.RootPath;
            });

            services.AddScoped<AgentPipelineBuilder>();
            services.AddScoped<AgentContext>();
            services.AddSingleton<AgentExecutionTracker>();

            services.AddHttpClient(string.Empty)
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
            
            services.AddSingleton(options.ModelChoice);
            
            services.AddScoped<IChatClient>(sp =>
            {
                var choice = sp.GetRequiredService<ModelChoice>();
                return ChatClientProviderExtensions.CreateChatClient(
                    sp.GetRequiredService<IHttpClientFactory>(), choice);
            });

            services.AddAutoRegisteredServices();

            // 注册 Agent 工厂相关服务
            services.AddSingleton<IAgentRegistry, AgentRegistry>();
            services.AddScoped<IAgentFactory, AgentFactory>();

            // Linux 使用 Bwarp 沙盒执行，Windows/macOS 直接 Process.Start
            if (OperatingSystem.IsLinux())
                services.AddScoped<IShellExecutor, BwarpShellExecutor>();
            else
                services.AddScoped<IShellExecutor, ProcessShellExecutor>();
            services.AddToolExecutor();
            services.AddToolMiddlewares();
            return services;
        }

        /// <summary>
        /// 使用 Fluent API 注册一个 Agent 定义。
        /// AgentDefinition 会以 Singleton 注册到 DI，并由 AgentRegistry 自动收集。
        /// </summary>
        public IServiceCollection AddAgent(string name, Action<AgentBuilder> configure)
        {
            var builder = new AgentBuilder(name);
            configure(builder);
            var definition = builder.Build();
            services.AddSingleton(definition);
            return services;
        }

        /// <summary>
        /// 注册所有内置 Agent 定义（General、Coder、Shell、Analyst）
        /// </summary>
        public IServiceCollection AddBuiltInAgents(AgentModelOptions? model = null)
        {
            services.AddSingleton(BuiltInAgents.General(model));
            services.AddSingleton(BuiltInAgents.Coder(model));
            services.AddSingleton(BuiltInAgents.Shell(model));
            services.AddSingleton(BuiltInAgents.Analyst(model));
            return services;
        }

        /// <summary>
        /// 从 ~/.man-in-black/settings.json 加载配置并注册所有服务。
        /// 设置文件变更会被自动跟踪；通过 IOptionsMonitor&lt;ManInBlackSettings&gt; 可获取最新值。
        /// </summary>
        public IServiceCollection AddManInBlackFromSettings(Action<ManInBlackOptions>? configure = null)
        {
            var configuration = ManInBlackConfigurationBuilder.BuildConfiguration();
            return services.AddManInBlackFromConfiguration(configuration, configure);
        }

        /// <summary>
        /// 从给定的 IConfiguration 加载配置并注册所有服务。
        /// 适用于已构建 WebApplicationBuilder 等场景，可复用其 Configuration。
        /// </summary>
        public IServiceCollection AddManInBlackFromConfiguration(
            IConfiguration configuration,
            Action<ManInBlackOptions>? configure = null)
        {
            services.Configure<ManInBlackSettings>(configuration);
            services.AddSingleton<IValidateOptions<ManInBlackSettings>, ValidateManInBlackSettings>();
            services.Configure<FeishuSettings>(configuration.GetSection("Feishu"));

            var settings = new ManInBlackSettings();
            configuration.Bind(settings);
            var modelChoice = settings.ToModelChoice();

            // 从配置加载 Agent 定义并注册到 DI，AgentRegistry 会自动收集
            AgentConfigurationLoader.LoadFromConfiguration(services, settings);

            return services.AddManInBlack(opt =>
            {
                opt.ModelChoice = modelChoice;
                configure?.Invoke(opt);
            });
        }
    }
}
