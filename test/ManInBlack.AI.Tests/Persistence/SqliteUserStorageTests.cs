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
    public async Task SaveUser_PersistsMetadataAndSessionIds()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var user = await storage.GetOrCreateUser("ext-1");
            user.Metadata["role"] = "admin";
            user.SessionIds.Add("ext-1_111");

            await storage.SaveUserAsync(user);

            var again = await storage.GetOrCreateUser("ext-1");
            Assert.Equal("admin", again.Metadata["role"].ToString());
            Assert.Contains("ext-1_111", again.SessionIds);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task CreateNewSessionId_AppendsAndPersists()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var sid = await storage.CreateNewSessionIdAsync("ext-1");

            Assert.StartsWith("ext-1_", sid);
            var again = await storage.GetOrCreateUser("ext-1");
            Assert.Contains(sid, again.SessionIds);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
