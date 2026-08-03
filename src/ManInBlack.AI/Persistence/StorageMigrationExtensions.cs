using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Persistence;

public static class StorageMigrationExtensions
{
    /// <summary>
    /// 数据搬迁必须执行在它之前（Prep+TimeTypes 已应用）、Finalize（删 SessionIdsJson 列）之前的那个 migration。
    /// Task 5 阶段最新 migration 即为此名。
    /// </summary>
    private const string LastPreFinalizeMigration = "NormalizeSessionsTimeTypes";

    /// <summary>
    /// 启动期显式应用 EF Core 迁移并设置 WAL。宿主在 BuildServiceProvider 之后调用一次。
    ///
    /// 分阶段 migrate：当 Finalize 尚未应用时（含旧库首次升级、以及本 Task 5 阶段 Finalize 还没生成这两种情况），
    /// 先 migrate 到 <see cref="LastPreFinalizeMigration"/>（保证 Sessions 表 + SessionIdsJson 列就位），
    /// 再跑幂等数据搬迁（blob → Sessions 行），最后 migrate 到最新（Task 7 起会应用 Finalize）。
    /// 若 Finalize 已应用（SessionIdsJson 列已删），跳过搬迁、直接 migrate 到最新——绝不降级已 Finalize 的库。
    /// </summary>
    public static async Task MigrateManInBlackStorageAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<ManInBlackDbContext>>();

        // Finalize 是否已应用？已应用 → blob 列已删，不能/不必搬迁。
        bool finalizeApplied;
        await using (var probe = factory.CreateDbContext())
        {
            var applied = await probe.Database.GetAppliedMigrationsAsync(ct);
            finalizeApplied = applied.Contains("NormalizeSessionsFinalize");
        }

        if (!finalizeApplied)
        {
            // migrate 到 LastPreFinalizeMigration（向上应用 Prep+TimeTypes；若已在其上则 no-op，绝不降级）
            await using var db0 = factory.CreateDbContext();
            var migrator = db0.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(LastPreFinalizeMigration, ct);
            await NormalizeSessionsDataMigration.RunAsync(factory, ct: ct);
        }

        // migrate 到最新（Task 5 阶段 = TimeTypes，no-op；Task 7 起应用 Finalize）
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync(ct);
        // WAL 为库级持久设置(已 WAL 时为 no-op);必须在无事务时设置
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }
}
