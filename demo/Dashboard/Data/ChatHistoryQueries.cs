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
        var groups = await db.SessionMessages.AsNoTracking()
            .GroupBy(x => x.SessionId)
            .Select(g => new
            {
                SessionId = g.Key,
                Count = g.Count(),
                First = g.Min(x => x.CreatedAt),
                Last = g.Max(x => x.CreatedAt),
            })
            .ToListAsync(ct);

        var userBySession = await BuildSessionToUserMapAsync(db, ct);

        return groups
            .OrderByDescending(g => g.Last)
            .Select(g => new SessionSummary
            {
                SessionId = g.SessionId,
                MessageCount = g.Count,
                FirstAt = g.First,
                LastAt = g.Last,
                UserId = userBySession.GetValueOrDefault(g.SessionId),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.Users.AsNoTracking().ToListAsync(ct);

        var list = new List<UserSummary>(rows.Count);
        foreach (var u in rows)
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(u.MetadataJson) ?? new();
                var sessionIds = JsonSerializer.Deserialize<List<string>>(u.SessionIdsJson) ?? new();
                list.Add(new UserSummary { UserId = u.UserId, Metadata = metadata, SessionIds = sessionIds });
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "用户 {UserId} 元数据反序列化失败,跳过", u.UserId);
            }
        }
        return list;
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
            CreatedAt = r.CreatedAt,
            Snippet = MakeSnippet(r.PayloadJson, query),
        }).ToList();
    }

    private static async Task<Dictionary<string, string>> BuildSessionToUserMapAsync(ManInBlackDbContext db, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        var map = new Dictionary<string, string>();
        foreach (var u in users)
        {
            try
            {
                var ids = JsonSerializer.Deserialize<List<string>>(u.SessionIdsJson);
                if (ids is null) continue;
                foreach (var sid in ids) map.TryAdd(sid, u.UserId);
            }
            catch (JsonException) { /* 忽略单用户解析失败 */ }
        }
        return map;
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
