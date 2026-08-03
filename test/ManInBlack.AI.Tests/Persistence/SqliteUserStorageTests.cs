using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class SqliteUserStorageTests
{
    private static SqliteUserStorage CreateStorage(IDbContextFactory<ManInBlackDbContext> factory) =>
        new(factory, NullLogger<SqliteUserStorage>.Instance);

    [Fact]
    public async Task GetOrCreateUser_CreatesThenReuses()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var u1 = await storage.GetOrCreateUser("ext-1");
            var u2 = await storage.GetOrCreateUser("ext-1");

            Assert.Equal("ext-1", u1.UserId);
            Assert.False(string.IsNullOrEmpty(u1.SelfHostUserId));
            Assert.Equal(u1.SelfHostUserId, u2.SelfHostUserId); // 复用而非新建
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task CreateNewSessionId_WritesSessionRow()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var sid = await storage.CreateNewSessionIdAsync("ext-1");
            await using var db = factory.CreateDbContext();
            Assert.True(await db.Sessions.AnyAsync(x => x.SessionId == sid));
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
