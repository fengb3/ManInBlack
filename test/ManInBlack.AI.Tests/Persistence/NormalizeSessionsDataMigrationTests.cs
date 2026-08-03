using ManInBlack.AI.Persistence;
using ManInBlack.AI.Persistence.Entities;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

using UserEntity = ManInBlack.AI.Persistence.Entities.UserEntity;
using SessionMessageEntity = ManInBlack.AI.Persistence.Entities.SessionMessageEntity;

namespace ManInBlack.AI.Tests.Persistence;

public class NormalizeSessionsDataMigrationTests
{
    [Fact]
    public async Task Run_MovesBlobToSessionsRows_AndResolvesOrphanToOwner()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                db.Users.Add(new UserEntity
                {
                    UserId = "u1",
                    SessionIdsJson = """["u1_1700000000"]""",
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
                db.SessionMessages.Add(new SessionMessageEntity { SessionId = "u1_1700000000", CreatedAt = DateTime.UtcNow, PayloadJson = "{}" });
                // orphan whose prefix matches a DIFFERENT real user
                db.Users.Add(new UserEntity { UserId = "u2", CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                db.SessionMessages.Add(new SessionMessageEntity { SessionId = "u2_1800000000", CreatedAt = DateTime.UtcNow, PayloadJson = "{}" });
                await db.SaveChangesAsync();
            }

            await NormalizeSessionsDataMigration.RunAsync(factory);

            await using var db2 = factory.CreateDbContext();
            var rows = await db2.Sessions.ToDictionaryAsync(x => x.SessionId);
            Assert.True(rows.ContainsKey("u1_1700000000"));        // from blob, owner u1
            Assert.True(rows.ContainsKey("u2_1800000000"));         // orphan resolved to owner u2
            Assert.All(rows.Values, r => Assert.Equal(0, r.Source)); // Interactive
            Assert.True(rows["u1_1700000000"].UserId > 0);
            Assert.True(rows["u2_1800000000"].UserId > 0);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task Run_DeletesUnresolvableOrphanMessages()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                db.SessionMessages.Add(new SessionMessageEntity { SessionId = "ghost_9999", CreatedAt = DateTime.UtcNow, PayloadJson = "{}" });
                await db.SaveChangesAsync();
            }
            // no user "ghost" exists → unresolvable orphan
            await NormalizeSessionsDataMigration.RunAsync(factory);

            await using var db2 = factory.CreateDbContext();
            Assert.False(await db2.Sessions.AnyAsync(x => x.SessionId == "ghost_9999"));
            Assert.False(await db2.SessionMessages.AnyAsync(x => x.SessionId == "ghost_9999")); // deleted
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task Run_IsIdempotent()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                db.Users.Add(new UserEntity { UserId = "u1", SessionIdsJson = """["u1_1"]""", CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            await NormalizeSessionsDataMigration.RunAsync(factory);
            await NormalizeSessionsDataMigration.RunAsync(factory);
            await using var db2 = factory.CreateDbContext();
            Assert.Single(await db2.Sessions.ToListAsync());
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
}
