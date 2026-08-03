using ManInBlack.AI.Persistence;
using ManInBlack.AI.Persistence.Entities;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

using SessionMessageEntity = ManInBlack.AI.Persistence.Entities.SessionMessageEntity;

namespace ManInBlack.AI.Tests.Persistence;

public class NormalizeSessionsDataMigrationTests
{
    /// <summary>
    /// 数据搬迁只在前-Finalize 的 schema 上运行（blob 列仍在）。TimeTypes 即 Finalize 前最后一个 migration。
    /// </summary>
    private const string PreFinalizeTarget = "NormalizeSessionsTimeTypes";

    [Fact]
    public async Task Run_MovesBlobToSessionsRows_AndResolvesOrphanToOwner()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync(PreFinalizeTarget);
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                // blob 列在 Finalize 前仍存在（NOT NULL），但实体已无该属性 → 用 raw SQL 种子。
                await SeedUserWithBlobAsync(db, "u1", """["u1_1700000000"]""");
                await SeedUserWithBlobAsync(db, "u2", "[]");
            }

            await using (var db = factory.CreateDbContext())
            {
                db.SessionMessages.Add(new SessionMessageEntity { SessionId = "u1_1700000000", CreatedAt = DateTime.UtcNow, PayloadJson = "{}" });
                // orphan whose prefix matches a DIFFERENT real user
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
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync(PreFinalizeTarget);
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

    /// <summary>
    /// 被引用的 sessionId 完全不含 '_'（LastIndexOf('_') = -1 → i&lt;=0）→ 前缀解析直接判负 → 真孤儿 → 消息删除。
    /// 区别于上一个用例（有 '_' 但前缀对不上用户），覆盖 ResolveOwnerByPrefixAsync 的 i&lt;=0 分支。
    /// </summary>
    [Fact]
    public async Task Run_DeletesOrphanMessages_WhenSessionIdHasNoUnderscoreSuffix()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync(PreFinalizeTarget);
        try
        {
            const string orphanSid = "plainnothsuffix"; // 无 '_'、无匹配用户
            await using (var db = factory.CreateDbContext())
            {
                db.SessionMessages.Add(new SessionMessageEntity { SessionId = orphanSid, CreatedAt = DateTime.UtcNow, PayloadJson = "{}" });
                db.SessionMessages.Add(new SessionMessageEntity { SessionId = orphanSid, CreatedAt = DateTime.UtcNow, PayloadJson = "{}" });
                await db.SaveChangesAsync();
            }

            await NormalizeSessionsDataMigration.RunAsync(factory);

            await using var db2 = factory.CreateDbContext();
            Assert.False(await db2.Sessions.AnyAsync(x => x.SessionId == orphanSid));
            Assert.False(await db2.SessionMessages.AnyAsync(x => x.SessionId == orphanSid)); // 真孤儿，消息被删
            Assert.Equal(0, await db2.Sessions.CountAsync()); // 整库没有任何会话行被建出来
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>
    /// 损坏的 SessionIdsJson blob（含非字符串元素 / 整体非法 JSON）应被静默跳过，搬迁继续不抛。
    /// </summary>
    [Theory]
    [InlineData("[123, 456]")]              // 数组里是非字符串 → 反序列化为 List&lt;string&gt; 抛 JsonException
    [InlineData("not a json array at all")] // 整体非 JSON
    [InlineData("{\"\"]")]                  // 截断/非法 JSON
    public async Task Run_SkipsCorruptSessionIdsBlob_WithoutThrowing(string corruptBlob)
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync(PreFinalizeTarget);
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                // 这个用户持有坏 blob；另一个用户持有正常 blob，用来验证坏的不影响好的
                await SeedUserWithBlobAsync(db, "good", """["good_1"]""");
                await SeedUserWithBlobAsync(db, "bad", corruptBlob);
            }

            // 不应抛
            var ex = await Record.ExceptionAsync(() => NormalizeSessionsDataMigration.RunAsync(factory));
            Assert.Null(ex);

            await using var db2 = factory.CreateDbContext();
            // 好 blob 正常搬迁；坏 blob 的 sessionId 不应出现
            var rows = await db2.Sessions.ToDictionaryAsync(x => x.SessionId);
            Assert.True(rows.ContainsKey("good_1"));
            Assert.False(rows.ContainsKey("123"));
            Assert.False(rows.ContainsKey("456"));
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }


    [Fact]
    public async Task Run_IsIdempotent()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync(PreFinalizeTarget);
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                await SeedUserWithBlobAsync(db, "u1", """["u1_1"]""");
            }
            await NormalizeSessionsDataMigration.RunAsync(factory);
            await NormalizeSessionsDataMigration.RunAsync(factory);
            await using var db2 = factory.CreateDbContext();
            Assert.Single(await db2.Sessions.ToListAsync());
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>
    /// blob 列(MetadataJson/SessionIdsJson)在实体上已删除,但 pre-Finalize schema 下仍是 NOT NULL。
    /// 经实体 INSERT 会缺这两列 → 用 raw SQL 一次 INSERT 全部 NOT NULL 列(仅 pre-Finalize schema 下可用)。
    /// </summary>
    private static async Task SeedUserWithBlobAsync(ManInBlackDbContext db, string userId, string sessionIdsJson)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Users (UserId, MetadataJson, SessionIdsJson, CreatedAt) VALUES ({0}, {1}, {2}, {3})",
            userId, "{}", sessionIdsJson, DateTime.UtcNow.ToString("o"));
    }
}
