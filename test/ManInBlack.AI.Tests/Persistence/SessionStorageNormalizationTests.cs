using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class SessionStorageNormalizationTests
{
    private static SqliteUserStorage CreateStorage(IDbContextFactory<ManInBlackDbContext> f) =>
        new(f, NullLogger<SqliteUserStorage>.Instance);

    [Fact]
    public async Task CreateNewSessionId_WritesSessionRow_InteractiveByDefault()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var sid = await storage.CreateNewSessionIdAsync("ext-1");

            await using var db = factory.CreateDbContext();
            var row = await db.Sessions.SingleAsync(x => x.SessionId == sid);
            Assert.Equal((int)SessionSource.Interactive, row.Source);
            Assert.StartsWith("ext-1_", row.SessionId);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task GetLatestSessionId_ReturnsLatestInteractive_ExcludesWebhook()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var oldInteractive = await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Interactive);
            await Task.Delay(50); // 仅保证 LastAt 有可见差异即可
            var webhook = await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Webhook);
            await Task.Delay(50);
            var newInteractive = await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Interactive);

            var latest = await storage.GetLatestSessionIdAsync("ext-1", SessionSource.Interactive);

            Assert.Equal(newInteractive, latest);            // 不是更新的 webhook
            Assert.NotEqual(webhook, latest);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task CreateNewSessionId_TwiceInSameSecond_DoesNotThrow()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var sid1 = await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Interactive);
            var sid2 = await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Interactive);

            Assert.NotEqual(sid1, sid2);
            Assert.StartsWith("ext-1_", sid1);
            Assert.StartsWith("ext-1_", sid2);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task GetLatestSessionId_ReturnsNull_WhenUserHasNoSessionOfSource()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Webhook);
            var latest = await storage.GetLatestSessionIdAsync("ext-1", SessionSource.Interactive);
            Assert.Null(latest);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
}
