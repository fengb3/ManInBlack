using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Persistence;

public static class StorageMigrationExtensions
{
    /// <summary>
    /// 启动期显式应用 EF Core 迁移并设置 WAL。宿主在 BuildServiceProvider 之后调用一次。
    /// </summary>
    public static async Task MigrateManInBlackStorageAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<ManInBlackDbContext>>();
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync(ct);
        // WAL 为库级持久设置(已 WAL 时为 no-op);必须在无事务时设置
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }
}
