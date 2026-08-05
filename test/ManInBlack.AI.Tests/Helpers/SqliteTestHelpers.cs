using ManInBlack.AI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Tests.Helpers;

public static class SqliteTestHelpers
{
    /// <summary>
    /// 在临时目录建一个已迁移到【最新】的 SQLite 工厂。返回 (工厂, ServiceProvider, 根路径)。
    /// 调用方负责释放 ServiceProvider 后清理根路径。
    /// </summary>
    public static Task<(IDbContextFactory<ManInBlackDbContext> factory, ServiceProvider sp, string rootPath)> CreateFactoryAsync()
        => CreateFactoryAsync(targetMigration: null);

    /// <summary>
    /// 在临时目录建一个已迁移到指定 <paramref name="targetMigration"/> 的 SQLite 工厂。
    /// 传 null 迁移到最新；用于数据搬迁测试时传 "NormalizeSessionsTimeTypes" 以保留 blob 列。
    /// </summary>
    public static async Task<(IDbContextFactory<ManInBlackDbContext> factory, ServiceProvider sp, string rootPath)> CreateFactoryAsync(string? targetMigration)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mib_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(root);
        var services = new ServiceCollection();
        services.AddDbContextFactory<ManInBlackDbContext>(o =>
            o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")}")
             .AddInterceptors(new ManInBlack.AI.Persistence.SqliteInitInterceptor()));
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<ManInBlackDbContext>>();
        await using var db = factory.CreateDbContext();
        if (targetMigration is null)
            await db.Database.MigrateAsync();
        else
            await db.Database.GetService<IMigrator>().MigrateAsync(targetMigration);
        return (factory, sp, root);
    }
}
