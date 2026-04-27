using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.AI;
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
            services.AddSingleton<IChatClient>(sp =>
            {
                var choice = sp.GetRequiredService<ModelChoice>();
                return ChatClientProviderExtensions.CreateChatClient(
                    sp.GetRequiredService<IHttpClientFactory>(), choice);
            });

            services.AddAutoRegisteredServices();
            services.AddToolExecutor();
            services.AddToolMiddlewares();
            return services;
        }

        /// <summary>
        /// 从 ~/.man-in-black/settings.json 加载配置并注册所有服务
        /// </summary>
        public IServiceCollection AddManInBlackFromSettings(Action<ManInBlackOptions>? configure = null)
        {
            var settings = SettingsLoader.Load();
            var modelChoice = settings.ToModelChoice();

            return services.AddManInBlack(opt =>
            {
                opt.ModelChoice = modelChoice;
                configure?.Invoke(opt);
            });
        }
    }
}
