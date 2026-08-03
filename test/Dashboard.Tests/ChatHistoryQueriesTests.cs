using System.Text.Json;
using Dashboard.Tests.Helpers;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Persistence.Entities;
using ManInBlack.Dashboard.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dashboard.Tests;

public class ChatHistoryQueriesTests
{
    private static ChatHistoryQueries NewQueries(IDbContextFactory<ManInBlackDbContext> factory) =>
        new(factory, NullLogger<ChatHistoryQueries>.Instance);

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListSessions_GroupsBySession_AndMapsUser()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await SeedAsync(factory);
            var q = NewQueries(factory);
            var sessions = await q.ListSessionsAsync();

            Assert.Equal(2, sessions.Count); // s1, s2
            var s1 = sessions.Single(s => s.SessionId == "s1");
            Assert.Equal(2, s1.MessageCount);
            Assert.Equal(T0, DateTime.Parse(s1.FirstAt, null, System.Globalization.DateTimeStyles.RoundtripKind));
            Assert.Equal(T0, DateTime.Parse(s1.LastAt, null, System.Globalization.DateTimeStyles.RoundtripKind)); // s1.LastAt 取自 Sessions.LastAt（种子 T0）
            Assert.Equal("u1", s1.UserId); // 关联用户
        }
        finally { sp.Dispose(); TryDelete(root); }
    }

    [Fact]
    public async Task ListSessions_ReturnsFromSessionsTable_WithSource()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                var user = new UserEntity { UserId = "u1" };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                db.Sessions.Add(new SessionEntity { SessionId = "u1_1", UserId = user.Id, Source = (int)SessionSource.Interactive, CreatedAt = T0, LastAt = T1 });
                db.SessionMessages.Add(new SessionMessageEntity { SessionId = "u1_1", CreatedAt = T0, PayloadJson = "{}" });
                await db.SaveChangesAsync();
            }
            var q = NewQueries(factory);
            var sessions = await q.ListSessionsAsync();
            var s = Assert.Single(sessions);
            Assert.Equal("u1_1", s.SessionId);
            Assert.Equal("u1", s.UserId);
            Assert.Equal((int)SessionSource.Interactive, s.Source);
            Assert.Equal(1, s.MessageCount);                  // 来自 SessionMessages 聚合
            Assert.Equal(T0, DateTime.Parse(s.FirstAt, null, System.Globalization.DateTimeStyles.RoundtripKind)); // 最早消息
            Assert.Equal(T1, DateTime.Parse(s.LastAt, null, System.Globalization.DateTimeStyles.RoundtripKind));  // 来自 Sessions.LastAt
        }
        finally { sp.Dispose(); TryDelete(root); }
    }

    [Fact]
    public async Task GetSessionMessages_SkipsCorruptRows()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await SeedAsync(factory);
            var q = NewQueries(factory);
            var s1 = await q.GetSessionMessagesAsync("s1");
            Assert.Equal(2, s1.Count); // 两条均解析
            var s2 = await q.GetSessionMessagesAsync("s2");
            Assert.Empty(s2); // 损坏行被跳过
        }
        finally { sp.Dispose(); TryDelete(root); }
    }

    [Fact]
    public async Task ListUsers_ReturnsUserIdAndCreatedAt()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await SeedAsync(factory);
            var q = NewQueries(factory);
            var users = await q.ListUsersAsync();
            var u = Assert.Single(users);
            Assert.Equal("u1", u.UserId);
            Assert.False(string.IsNullOrEmpty(u.CreatedAt));
        }
        finally { sp.Dispose(); TryDelete(root); }
    }

    [Fact]
    public async Task Search_HitsByPayload_AndEmptyQueryReturnsNothing()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await SeedAsync(factory);
            var q = NewQueries(factory);
            var hits = await q.SearchAsync("hello");
            Assert.Contains(hits, r => r.SessionId == "s1");
            Assert.Empty(await q.SearchAsync(""));
        }
        finally { sp.Dispose(); TryDelete(root); }
    }

    private static async Task SeedAsync(IDbContextFactory<ManInBlackDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        var user = new UserEntity { UserId = "u1" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.Sessions.Add(new SessionEntity { SessionId = "s1", UserId = user.Id, Source = (int)SessionSource.Interactive, CreatedAt = T0, LastAt = T0 });
        db.Sessions.Add(new SessionEntity { SessionId = "s2", UserId = user.Id, Source = (int)SessionSource.Webhook, CreatedAt = T2, LastAt = T2 });

        db.SessionMessages.Add(new SessionMessageEntity { SessionId = "s1", CreatedAt = T0, PayloadJson = JsonSerializer.Serialize(new ChatMessage(ChatRole.User, "hello")) });
        db.SessionMessages.Add(new SessionMessageEntity { SessionId = "s1", CreatedAt = T1, PayloadJson = JsonSerializer.Serialize(new ChatMessage(ChatRole.Assistant, "hi")) });
        db.SessionMessages.Add(new SessionMessageEntity { SessionId = "s2", CreatedAt = T2, PayloadJson = "{not-json" });
        await db.SaveChangesAsync();
    }

    private static void TryDelete(string root) { try { Directory.Delete(root, true); } catch (IOException) { } }
}
