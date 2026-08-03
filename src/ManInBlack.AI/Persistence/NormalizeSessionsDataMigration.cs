using System.Text.Json;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// 正规化数据搬迁：把旧 <c>Users.SessionIdsJson</c> blob 拆成 <see cref="SessionEntity"/> 行，
/// 并为孤儿 <see cref="SessionMessageEntity"/>（引用但不在任何 blob 里）补建会话行。
/// 在 <c>NormalizeSessionsPrep/TimeTypes</c> migration 之后、<c>NormalizeSessionsFinalize</c>（删 blob）之前执行。
/// 幂等：按 <see cref="SessionEntity.SessionId"/> 存在性跳过；用 ADO.NET 读 blob（实体在 Finalize 后无该列）。
/// </summary>
public static class NormalizeSessionsDataMigration
{
    private record BlobUser(long Id, string SessionIdsJson);

    /// <summary>
    /// 执行一次幂等的 blob → Sessions 搬迁。
    /// </summary>
    public static async Task RunAsync(
        IDbContextFactory<ManInBlackDbContext> dbFactory,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();

        // 1) 已有的 Sessions（含本程序运行期间由 CreateNewSessionIdAsync/SaveMessage 写入的）—— 用来去重
        var existing = (await db.Sessions.AsNoTracking()
                .Select(x => x.SessionId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        // 2) 读 Users.SessionIdsJson（ADO.NET：列在 Finalize 前仍存在，实体在 Finalize 后无该属性）
        var blobUsers = await ReadBlobUsersAsync(db, ct);

        var now = DateTime.UtcNow;

        // 3) 常规搬迁：每个用户 blob 里的 sessionId → 以【该用户行】为归属写入 Sessions
        foreach (var bu in blobUsers)
        {
            List<string>? sids = null;
            try { sids = JsonSerializer.Deserialize<List<string>>(bu.SessionIdsJson); }
            catch (JsonException) { /* 坏 blob 跳过 */ }

            if (sids is null) continue;

            foreach (var sid in sids)
            {
                if (string.IsNullOrEmpty(sid)) continue;
                if (!existing.Add(sid)) continue;   // 已存在（含本批已加）

                var lastAt = await MaxMessageCreatedAtAsync(db, sid, ct) ?? now;
                db.Sessions.Add(new SessionEntity
                {
                    SessionId = sid,
                    UserId = bu.Id,                       // blob 的归属 = 该用户行
                    Source = (int)SessionSource.Interactive,
                    CreatedAt = ParseCreatedAt(sid, now),
                    LastAt = lastAt,
                });
            }
        }

        // 4) 孤儿：SessionMessages/Snapshots 引用、但不在任何 blob 里的 sessionId。
        //    归属按 sessionId 的 {userId}_{ts} 前缀解析；解析不到真实用户 → 删其消息/快照（ownerless 垃圾，
        //    否则 Task 7 的 FK 会失败）。
        var referenced = await db.SessionMessages.AsNoTracking()
                .Select(m => m.SessionId)
                .Distinct()
                .ToListAsync(ct);
        var referencedSnapshots = await db.AgentStateSnapshots.AsNoTracking()
                .Select(s => s.SessionId)
                .Distinct()
                .ToListAsync(ct);
        referenced.AddRange(referencedSnapshots);

        var orphanSessionIds = referenced.Distinct(StringComparer.Ordinal)
            .Where(s => !existing.Contains(s))
            .ToList();

        var deletedMessages = 0;
        var deletedSnapshots = 0;
        foreach (var sid in orphanSessionIds)
        {
            var ownerId = await ResolveOwnerByPrefixAsync(db, sid, ct);
            if (ownerId is null)
            {
                // 真孤儿：删 ownerless 消息/快照
                var msgs = db.SessionMessages.Where(m => m.SessionId == sid);
                deletedMessages += await msgs.CountAsync(ct);
                db.SessionMessages.RemoveRange(msgs);
                var snaps = db.AgentStateSnapshots.Where(s => s.SessionId == sid);
                deletedSnapshots += await snaps.CountAsync(ct);
                db.AgentStateSnapshots.RemoveRange(snaps);
                continue;
            }

            if (!existing.Add(sid)) continue;

            var lastAt = await MaxMessageCreatedAtAsync(db, sid, ct) ?? now;
            db.Sessions.Add(new SessionEntity
            {
                SessionId = sid,
                UserId = ownerId.Value,
                Source = (int)SessionSource.Interactive,
                CreatedAt = lastAt,
                LastAt = lastAt,
            });
        }

        if (deletedMessages > 0 || deletedSnapshots > 0)
        {
            logger?.LogWarning(
                "数据搬迁删除无主孤儿数据：SessionMessages={Msgs} 行，AgentStateSnapshots={Snaps} 行",
                deletedMessages, deletedSnapshots);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<BlobUser>> ReadBlobUsersAsync(ManInBlackDbContext db, CancellationToken ct)
    {
        var result = new List<BlobUser>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, SessionIdsJson FROM Users";
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                result.Add(new BlobUser(
                    rdr.GetInt64(0),
                    rdr.IsDBNull(1) ? "[]" : rdr.GetString(1)));
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
        return result;
    }

    private static async Task<DateTime?> MaxMessageCreatedAtAsync(ManInBlackDbContext db, string sessionId, CancellationToken ct)
        => await db.SessionMessages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .MaxAsync(m => (DateTime?)m.CreatedAt, ct);

    /// <summary>
    /// 按 sessionId 形如 {userId}_{ts} 的前缀查归属用户的自增 Id。
    /// 返回 null 表示前缀对不上任何真实用户（真孤儿）。
    /// </summary>
    private static async Task<long?> ResolveOwnerByPrefixAsync(ManInBlackDbContext db, string sessionId, CancellationToken ct)
    {
        var i = sessionId.LastIndexOf('_');
        if (i <= 0) return null;
        var prefix = sessionId[..i];
        var owner = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == prefix, ct);
        return owner?.Id;
    }

    private static DateTime ParseCreatedAt(string sessionId, DateTime fallback)
    {
        var i = sessionId.LastIndexOf('_');
        if (i >= 0 && i + 1 < sessionId.Length && long.TryParse(sessionId[(i + 1)..], out var secs))
            return DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
        return fallback;
    }
}
