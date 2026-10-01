using ManInBlack.AI.Abstraction.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// SQLite 持久化包的可选 DI 扩展。
/// </summary>
public static class SqlitePersistenceDependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// 注册 ManInBlack 的 SQLite 持久化实现，覆盖主包内置的内存存储。
        /// 需在 <c>services.AddManInBlack()</c> 之后调用，以确保 SQLite 实现取代默认内存实现。
        /// </summary>
        public IServiceCollection AddManInBlackSqlitePersistence()
        {
            // DbContext 工厂：连接串从 RootPath 取，并设置 busy_timeout
            services.AddDbContextFactory<ManInBlackDbContext>((sp, o) =>
            {
                var root = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.RootPath;
                Directory.CreateDirectory(root);
                o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")}");
                o.AddInterceptors(new SqliteInitInterceptor());
            });

            // SQLite 存储实现覆盖主包的内存默认实现
            services.AddSingleton<IUserStorage, SqliteUserStorage>();
            services.AddSingleton<ISessionStorage, SqliteAgentStateStorage>();
            services.AddSingleton<IAgentStateStorage, SqliteAgentStateStorage>();

            // 一次性 JSON → SQLite 迁移工具
            services.TryAddSingleton<JsonToSqliteMigrator>();

            return services;
        }
    }
}
