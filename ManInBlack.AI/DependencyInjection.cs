using ManInBlack.AI.Middleware;

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
    }
}