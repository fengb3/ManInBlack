using System.Text.Json;
using ManInBlack.AI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.Dashboard.Data;

/// <summary>直查 DbContext 的只读历史查询(会话/用户/单会话/搜索)。</summary>
public sealed class ChatHistoryQueries(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<ChatHistoryQueries> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        // 直查 Sessions 表(已正规化),关联 Users 取 UserId;
        // MessageCount / FirstAt 由 SessionMessages 关联子查询聚合后,在内存里格式化为 string ReadModel。
        var rows = await db.Sessions.AsNoTracking()
            .Join(db.Users,
                s => s.UserId,
                u => u.Id,
                (s, u) => new
                {
                    s.SessionId,
                    s.LastAt,
                    s.Source,
                    UserId = u.UserId,
                    MessageCount = db.SessionMessages.Count(m => m.SessionId == s.SessionId),
                    FirstAt = db.SessionMessages
                        .Where(m => m.SessionId == s.SessionId)
                        .Min(m => (DateTime?)m.CreatedAt),
                })
            .OrderByDescending(x => x.LastAt)
            .ToListAsync(ct);

        return rows
            .Select(x => new SessionSummary
            {
                SessionId = x.SessionId,
                MessageCount = x.MessageCount,
                FirstAt = x.FirstAt?.ToString("O") ?? "",
                LastAt = x.LastAt.ToString("O"),
                UserId = x.UserId,
                Source = x.Source,
            })
            .ToList();
    }

    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Users.AsNoTracking()
            .Select(u => new UserSummary { UserId = u.UserId, CreatedAt = u.CreatedAt.ToString("O") })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MessageView>> GetSessionMessagesAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.SessionMessages.AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        var views = new List<MessageView>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<ChatMessage>(row.PayloadJson, JsonOptions);
                if (msg is not null) views.Add(ChatMessageRenderer.Render(msg));
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "会话 {SessionId} 第 {Id} 行消息反序列化失败,跳过", sessionId, row.Id);
            }
        }
        return views;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchResult>();

        await using var db = dbFactory.CreateDbContext();
        var rows = await db.SessionMessages.AsNoTracking()
            .Where(x => EF.Functions.Like(x.PayloadJson, $"%{query}%"))
            .OrderByDescending(x => x.Id)
            .Take(200)
            .Select(x => new { x.SessionId, x.PayloadJson, x.CreatedAt })
            .ToListAsync(ct);

        return rows.Select(r => new SearchResult
        {
            SessionId = r.SessionId,
            CreatedAt = r.CreatedAt.ToString("O"),
            Snippet = MakeSnippet(r.PayloadJson, query),
        }).ToList();
    }

    private static string MakeSnippet(string payload, string query, int radius = 60)
    {
        var idx = payload.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return payload.Length <= 120 ? payload : payload[..120];
        var start = Math.Max(0, idx - radius);
        var len = Math.Min(payload.Length - start, query.Length + radius * 2);
        var snippet = payload.Substring(start, len);
        return (start > 0 ? "…" : "") + snippet + (start + len < payload.Length ? "…" : "");
    }
}
