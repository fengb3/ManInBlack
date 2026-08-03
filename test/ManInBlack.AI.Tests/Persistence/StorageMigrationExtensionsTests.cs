using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

/// <summary>
/// 端到端验证 <see cref="StorageMigrationExtensions.MigrateManInBlackStorageAsync"/> 编排器:
/// 旧库(仅 InitialCreate)经分阶段升级后,Finalize 已应用、blob 已搬入 Sessions、孤儿已清;
/// 且重跑不降级、幂等。覆盖分阶段 migrate 的探测/分支逻辑(数据搬迁单测覆盖不到的部分)。
/// </summary>
public class StorageMigrationExtensionsTests
{
    [Fact]
    public async Task MigrateManInBlackStorage_UpgradesLegacyDb_StagesMoveThenFinalize()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync("InitialCreate");
        try
        {
            // 1) 旧 schema(InitialCreate:blob 列在、无 Sessions 表、SessionMessages 无 FK)种子:
            //    一个用户带 SessionIdsJson blob + 真会话消息 + 孤儿消息(前缀 ghost 无对应用户)
            await using (var seed = factory.CreateDbContext())
            {
                await seed.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Users (UserId, MetadataJson, SessionIdsJson) VALUES ({0}, {1}, {2})",
                    "u1", "{}", """["u1_1700000000"]""");
                await seed.Database.ExecuteSqlRawAsync(
                    "INSERT INTO SessionMessages (SessionId, CreatedAt, PayloadJson) VALUES ({0}, {1}, {2})",
                    "u1_1700000000", "2024-01-01T00:00:00Z", "{}");
                await seed.Database.ExecuteSqlRawAsync(
                    "INSERT INTO SessionMessages (SessionId, CreatedAt, PayloadJson) VALUES ({0}, {1}, {2})",
                    "ghost_9999", "2024-01-01T00:00:00Z", "{}");
            }

            // 2) 跑编排器(分阶段:migrate→TimeTypes、搬数据、migrate→Finalize)
            await sp.MigrateManInBlackStorageAsync();

            // 3) Finalize 已应用 + 数据搬迁正确
            await using var db = factory.CreateDbContext();
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains(applied, m => m.Contains("NormalizeSessionsFinalize"));

            Assert.True(await db.Sessions.AnyAsync(x => x.SessionId == "u1_1700000000"));    // blob → Sessions
            Assert.False(await db.Sessions.AnyAsync(x => x.SessionId == "ghost_9999"));       // 孤儿未建会话
            Assert.False(await db.SessionMessages.AnyAsync(x => x.SessionId == "ghost_9999")); // 孤儿消息已删
            Assert.True(await db.SessionMessages.AnyAsync(x => x.SessionId == "u1_1700000000")); // 真会话消息保留

            // 4) 幂等重跑:不抛、不降级(Finalize 仍最新)、Sessions 不重复
            await sp.MigrateManInBlackStorageAsync();
            var applied2 = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains(applied2, m => m.Contains("NormalizeSessionsFinalize"));
            Assert.Equal(1, await db.Sessions.CountAsync(x => x.SessionId == "u1_1700000000"));
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>
    /// 旧 blob 可能有重复 sessionId（老 CreateNewSessionIdAsync 同秒创建未去重，真实数据踩到过）。
    /// Finalize 的搬迁用 INSERT OR IGNORE 去重，重复项按唯一索引跳过，只留一行。
    /// </summary>
    [Fact]
    public async Task MigrateManInBlackStorage_DedupsDuplicateBlobSessionIds()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync("InitialCreate");
        try
        {
            await using (var seed = factory.CreateDbContext())
            {
                await seed.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Users (UserId, MetadataJson, SessionIdsJson) VALUES ({0}, {1}, {2})",
                    "u1", "{}", """["u1_1700000000", "u1_1700000000"]""");
                await seed.Database.ExecuteSqlRawAsync(
                    "INSERT INTO SessionMessages (SessionId, CreatedAt, PayloadJson) VALUES ({0}, {1}, {2})",
                    "u1_1700000000", "2024-01-01T00:00:00Z", "{}");
            }

            await sp.MigrateManInBlackStorageAsync();

            await using var db = factory.CreateDbContext();
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains(applied, m => m.Contains("NormalizeSessionsFinalize"));
            Assert.Equal(1, await db.Sessions.CountAsync(x => x.SessionId == "u1_1700000000")); // 重复 → 去重为一行
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
}
