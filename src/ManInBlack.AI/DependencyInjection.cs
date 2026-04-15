using ManInBlack.AI;
using ManInBlack.AI.Middleware;
using Microsoft.Extensions.AI;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;


public class ManInBlackOptions
{
    public ModelChoice ModelChoice { get; set; } = default!;
}

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// 注册 ManInBlack 核心服务（AgentPipeline、AgentContext）
        /// </summary>
        public IServiceCollection AddManInBlack(Action<ManInBlackOptions> configure)
        {

            var options = new ManInBlackOptions();
            configure(options);

            services.AddScoped<AgentPipelineBuilder>();
            services.AddScoped<AgentContext>();

            services.AddHttpClient();
            services.AddSingleton(options.ModelChoice);
            services.AddSingleton<IChatClient>(sp =>
            {
                var choice = sp.GetRequiredService<ModelChoice>();
                return ChatClientProviderExtensions.CreateChatClient(
                    sp.GetRequiredService<IHttpClientFactory>(), choice);
            });
            return services;
        }
    }
}
