using ManInBlack.AI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Tests.Helpers;

public static class SqliteTestHelpers
{
    /// <summary>
    /// 在临时目录建一个已迁移的 SQLite 工厂。返回 (工厂, ServiceProvider, 根路径)。
    /// 调用方负责释放 ServiceProvider 后清理根路径。
    /// </summary>
    public static async Task<(IDbContextFactory<ManInBlackDbContext> factory, ServiceProvider sp, string rootPath)> CreateFactoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mib_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(root);
        var services = new ServiceCollection();
        services.AddDbContextFactory<ManInBlackDbContext>(o =>
            o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")}"));
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<ManInBlackDbContext>>();
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        return (factory, sp, root);
    }
}
