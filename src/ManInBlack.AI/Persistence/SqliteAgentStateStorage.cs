using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// SQLite 实现的会话消息 + 状态快照存储。
/// </summary>
public class SqliteAgentStateStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteAgentStateStorage> logger) : IAgentStateStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task SaveMessage(string sessionId, ChatMessage message)
    {
        await using var db = dbFactory.CreateDbContext();
        db.SessionMessages.Add(new SessionMessageEntity
        {
            SessionId = sessionId,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            PayloadJson = JsonSerializer.Serialize(message, JsonOptions),
        });
        await db.SaveChangesAsync();
    }

    public async Task<IList<ChatMessage>> LoadMessages(string sessionId)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.SessionMessages
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var messages = new List<ChatMessage>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<ChatMessage>(row.PayloadJson, JsonOptions);
                if (msg is not null) messages.Add(msg);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "会话 {SessionId} 第 {Id} 行消息反序列化失败,跳过", sessionId, row.Id);
            }
        }
        return messages;
    }

    public async Task<AgentStateSnapshot?> LoadSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var row = await db.AgentStateSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);

        if (row is null) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentStateSnapshot>(row.PayloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "快照 {SessionId} 反序列化失败,返回 null", sessionId);
            return null;
        }
    }

    public async Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var existing = await db.AgentStateSnapshots.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        var savedAt = (snapshot.SavedAt == default ? DateTimeOffset.UtcNow : snapshot.SavedAt).ToString("O");
        var payload = JsonSerializer.Serialize(snapshot, JsonOptions);

        if (existing is null)
        {
            db.AgentStateSnapshots.Add(new AgentStateSnapshotEntity
            {
                SessionId = sessionId,
                SavedAt = savedAt,
                PayloadJson = payload,
            });
        }
        else
        {
            existing.SavedAt = savedAt;
            existing.PayloadJson = payload;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var row = await db.AgentStateSnapshots.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        if (row is not null)
        {
            db.AgentStateSnapshots.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }
}
