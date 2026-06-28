using ManInBlack.AI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dashboard.Tests.Helpers;

public static class SqliteTestHelper
{
    /// <summary>临时目录建一个已迁移、可写的 SQLite 工厂(供测试种子)。调用方负责释放 sp 后清理 root。</summary>
    public static async Task<(IDbContextFactory<ManInBlackDbContext> factory, ServiceProvider sp, string rootPath)> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mib_dash_{Guid.NewGuid()}");
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
