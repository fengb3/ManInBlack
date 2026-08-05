using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Persistence;

public static class StorageMigrationExtensions
{
    /// <summary>
    /// 启动期显式应用 EF Core 迁移并设置 WAL。宿主在 BuildServiceProvider 之后调用一次。
    ///
    /// 旧 Users.SessionIdsJson blob → Sessions 行的数据搬迁已内置于 <c>NormalizeSessionsFinalize</c>
    /// migration 的 <c>Up</c>（migrationBuilder.Sql + json_each，在加 FK / 删 blob 列之前执行），
    /// 故 <see cref="DatabaseFacade.MigrateAsync"/> 即可完整升级；<c>dotnet ef database update</c> 同样可用，
    /// 无需分阶段启动逻辑。
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
