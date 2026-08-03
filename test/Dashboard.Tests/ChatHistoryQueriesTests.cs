using System.Text.Json;
using Dashboard.Tests.Helpers;
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
            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.Parse(s1.FirstAt, null, System.Globalization.DateTimeStyles.RoundtripKind));
            Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), DateTime.Parse(s1.LastAt, null, System.Globalization.DateTimeStyles.RoundtripKind));
            Assert.Equal("u1", s1.UserId); // 关联用户
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
    public async Task ListUsers_DeserializesSessionsAndMetadata()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await SeedAsync(factory);
            var q = NewQueries(factory);
            var users = await q.ListUsersAsync();
            var u = Assert.Single(users);
            Assert.Equal("u1", u.UserId);
            Assert.Contains("s1", u.SessionIds);
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
        db.SessionMessages.Add(new SessionMessageEntity { SessionId = "s1", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), PayloadJson = JsonSerializer.Serialize(new ChatMessage(ChatRole.User, "hello")) });
        db.SessionMessages.Add(new SessionMessageEntity { SessionId = "s1", CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), PayloadJson = JsonSerializer.Serialize(new ChatMessage(ChatRole.Assistant, "hi")) });
        db.SessionMessages.Add(new SessionMessageEntity { SessionId = "s2", CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), PayloadJson = "{not-json" });
        db.Users.Add(new UserEntity { UserId = "u1", MetadataJson = "{}", SessionIdsJson = JsonSerializer.Serialize(new List<string> { "s1" }) });
        await db.SaveChangesAsync();
    }

    private static void TryDelete(string root) { try { Directory.Delete(root, true); } catch (IOException) { } }
}
