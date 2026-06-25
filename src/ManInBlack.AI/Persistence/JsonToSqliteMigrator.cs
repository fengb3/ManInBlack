using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// 一次性把旧 JSON 文件导入 SQLite。幂等:按 sessionId / userId 存在性跳过。
/// 旧布局:{RootPath}/sessions/*.jsonl、*.state.json、{RootPath}/users/userIdMap.json + {数字id}.json
/// </summary>
[ServiceRegister.Singleton]
public class JsonToSqliteMigrator(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    IOptions<AgentStorageOptions> options,
    ILogger<JsonToSqliteMigrator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 执行迁移：读取旧 JSON 文件并导入 SQLite。已存在的记录会被跳过（幂等）。
    /// </summary>
    public async Task<MigrationSummary> MigrateAsync(CancellationToken ct = default)
    {
        var root = options.Value.RootPath;
        var sessionsDir = Path.Combine(root, "sessions");
        var usersDir = Path.Combine(root, "users");
        int msg = 0, snap = 0, usr = 0, skip = 0;

        await using var db = dbFactory.CreateDbContext();

        // 1) 会话历史 JSONL
        if (Directory.Exists(sessionsDir))
        {
            foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.jsonl"))
            {
                var sessionId = Path.GetFileNameWithoutExtension(file);
                if (await db.SessionMessages.AnyAsync(x => x.SessionId == sessionId, ct)) { skip++; continue; }

                var now = DateTimeOffset.UtcNow.ToString("O");
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                foreach (var line in await File.ReadAllLinesAsync(file, ct))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var m = JsonSerializer.Deserialize<ChatMessage>(line, JsonOptions);
                        if (m is null) continue;
                        db.SessionMessages.Add(new SessionMessageEntity
                        {
                            SessionId = sessionId,
                            CreatedAt = now,
                            PayloadJson = JsonSerializer.Serialize(m, JsonOptions),
                        });
                        msg++;
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "迁移:会话 {SessionId} 跳过坏行", sessionId);
                    }
                }
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }

        // 2) 状态快照
        if (Directory.Exists(sessionsDir))
        {
            foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.state.json"))
            {
                var sessionId = Path.GetFileName(file).Replace(".state.json", "");
                if (await db.AgentStateSnapshots.AnyAsync(x => x.SessionId == sessionId, ct)) { skip++; continue; }

                try
                {
                    var s = JsonSerializer.Deserialize<AgentStateSnapshot>(await File.ReadAllTextAsync(file, ct), JsonOptions);
                    if (s is null) continue;
                    db.AgentStateSnapshots.Add(new AgentStateSnapshotEntity
                    {
                        SessionId = sessionId,
                        SavedAt = (s.SavedAt == default ? DateTimeOffset.UtcNow : s.SavedAt).ToString("O"),
                        PayloadJson = JsonSerializer.Serialize(s, JsonOptions),
                    });
                    await db.SaveChangesAsync(ct);
                    snap++;
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "迁移:快照 {SessionId} 跳过(损坏)", sessionId);
                }
            }
        }

        // 3) 用户(userIdMap + 条目)
        var mapFile = Path.Combine(usersDir, "userIdMap.json");
        if (File.Exists(mapFile))
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(mapFile, ct), JsonOptions) ?? new();
            foreach (var (oriId, internalId) in map)
            {
                if (await db.Users.AnyAsync(x => x.UserId == oriId, ct)) { skip++; continue; }

                string meta = "{}", sids = "[]";
                var entryFile = Path.Combine(usersDir, $"{internalId}.json");
                if (File.Exists(entryFile))
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<UserEntry>(await File.ReadAllTextAsync(entryFile, ct), JsonOptions);
                        if (entry is not null)
                        {
                            meta = JsonSerializer.Serialize(entry.Metadata ?? new(), JsonOptions);
                            sids = JsonSerializer.Serialize(entry.SessionIds ?? new List<string>(), JsonOptions);
                        }
                    }
                    catch (JsonException ex) { logger.LogWarning(ex, "迁移:用户 {Id} 条目损坏,用空值", oriId); }
                }

                db.Users.Add(new UserEntity
                {
                    Id = int.Parse(internalId), // 保留原数字内部 id
                    UserId = oriId,
                    MetadataJson = meta,
                    SessionIdsJson = sids,
                });
                await db.SaveChangesAsync(ct);
                usr++;
            }
        }

        return new MigrationSummary(msg, snap, usr, skip);
    }
}

/// <summary>
/// 迁移汇总。
/// </summary>
public sealed record MigrationSummary(int Messages, int Snapshots, int Users, int Skipped);
