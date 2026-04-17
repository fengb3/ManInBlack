using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// 注册 ManInBlack 核心服务 以及基础中间件 和 工具
        /// </summary>
        public IServiceCollection AddManInBlack(Action<ManInBlackOptions> configure)
        {
            services.AddManInBlackCore(configure);
            services.AddAutoRegisteredServices();
            services.AddToolExecutor();
            return services;
        }
    }
}