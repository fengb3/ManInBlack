using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class DbContextSmokeTests
{
    [Fact]
    public async Task Migrate_ShouldCreateAllThreeTables()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            await using var db = factory.CreateDbContext();
            var tables = await db.Database
                .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")
                .ToListAsync();

            Assert.Contains("SessionMessages", tables);
            Assert.Contains("AgentStateSnapshots", tables);
            Assert.Contains("Users", tables);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
