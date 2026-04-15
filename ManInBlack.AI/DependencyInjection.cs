using ManInBlack.AI;
using ManInBlack.AI.Middleware;
using Microsoft.Extensions.AI;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddManInBlack()
        {
            services.AddScoped<AgentPipeline>();
            services.AddScoped<AgentContext>();
            return services;
        }

        /// <summary>
        /// 注册 ModelChoice 并自动创建对应的 IChatClient
        /// </summary>
        public IServiceCollection AddManInBlackChatClient(ModelChoice modelChoice)
        {
            services.AddHttpClient();
            services.AddSingleton(modelChoice);
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
