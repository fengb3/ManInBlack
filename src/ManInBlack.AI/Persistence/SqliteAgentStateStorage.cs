using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// SQLite 实现的会话消息 + 状态快照存储。本任务先实现消息部分;快照部分见 Task 3。
/// </summary>
public class SqliteAgentStateStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteAgentStateStorage> logger)
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
}
