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

        // 1) 会话历史 JSONL（整段一个事务）
        if (Directory.Exists(sessionsDir))
        {
            await using var msgTx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.jsonl"))
                {
                    var sessionId = Path.GetFileNameWithoutExtension(file);
                    if (await db.SessionMessages.AnyAsync(x => x.SessionId == sessionId, ct)) { skip++; continue; }

                    var now = DateTime.UtcNow;
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
                }
                await db.SaveChangesAsync(ct);
                await msgTx.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await msgTx.RollbackAsync(ct);
                logger.LogError(ex, "迁移:会话历史事务失败,已回滚");
            }
        }

        // 2) 状态快照（整段一个事务）
        if (Directory.Exists(sessionsDir))
        {
            await using var snapTx = await db.Database.BeginTransactionAsync(ct);
            try
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
                            SavedAt = s.SavedAt == default ? DateTimeOffset.UtcNow.UtcDateTime : s.SavedAt.UtcDateTime,
                            PayloadJson = JsonSerializer.Serialize(s, JsonOptions),
                        });
                        snap++;
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "迁移:快照 {SessionId} 跳过(损坏)", sessionId);
                    }
                }
                await db.SaveChangesAsync(ct);
                await snapTx.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await snapTx.RollbackAsync(ct);
                logger.LogError(ex, "迁移:状态快照事务失败,已回滚");
            }
        }

        // 3) 用户(userIdMap + 条目)（整段一个事务）
        var mapFile = Path.Combine(usersDir, "userIdMap.json");
        if (File.Exists(mapFile))
        {
            Dictionary<string, string> map;
            try
            {
                map = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(mapFile, ct), JsonOptions) ?? new();
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "迁移:userIdMap.json 损坏,跳过用户迁移");
                return new MigrationSummary(msg, snap, usr, skip);
            }

            await using var usrTx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var (oriId, internalId) in map)
                {
                    if (await db.Users.AnyAsync(x => x.UserId == oriId, ct)) { skip++; continue; }

                    if (!int.TryParse(internalId, out var internalIdNum))
                    {
                        logger.LogWarning("迁移:用户 {OriId} 的内部 id '{InternalId}' 非数字,跳过", oriId, internalId);
                        skip++;
                        continue;
                    }

                    db.Users.Add(new UserEntity
                    {
                        Id = internalIdNum,
                        UserId = oriId,
                        CreatedAt = DateTime.UtcNow,
                    });

                    // 旧条目里的 SessionIds → 写 Sessions 行（正规化后不再写 SessionIdsJson blob）。
                    var entryFile = Path.Combine(usersDir, $"{internalId}.json");
                    if (File.Exists(entryFile))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(entryFile, ct));
                            if (doc.RootElement.TryGetProperty("SessionIds", out var sidsEl) && sidsEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var sidEl in sidsEl.EnumerateArray())
                                {
                                    var sid = sidEl.GetString();
                                    if (string.IsNullOrEmpty(sid)) continue;
                                    if (await db.Sessions.AnyAsync(x => x.SessionId == sid, ct)) continue;
                                    db.Sessions.Add(new SessionEntity
                                    {
                                        SessionId = sid,
                                        UserId = internalIdNum,
                                        Source = (int)SessionSource.Interactive,
                                        CreatedAt = DateTime.UtcNow,
                                        LastAt = DateTime.UtcNow,
                                    });
                                }
                            }
                        }
                        catch (JsonException ex) { logger.LogWarning(ex, "迁移:用户 {Id} 条目损坏,用空值", oriId); }
                    }
                    usr++;
                }
                await db.SaveChangesAsync(ct);
                await usrTx.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await usrTx.RollbackAsync(ct);
                logger.LogError(ex, "迁移:用户事务失败,已回滚");
            }
        }

        return new MigrationSummary(msg, snap, usr, skip);
    }
}

/// <summary>
/// 迁移汇总。
/// </summary>
public sealed record MigrationSummary(int Messages, int Snapshots, int Users, int Skipped);
