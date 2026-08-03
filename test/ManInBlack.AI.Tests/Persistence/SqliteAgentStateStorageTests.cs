using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class SqliteAgentStateStorageTests
{
    private static SqliteAgentStateStorage CreateStorage(IDbContextFactory<ManInBlackDbContext> factory) =>
        new(factory, NullLogger<SqliteAgentStateStorage>.Instance);

    [Fact]
    public async Task SaveMessage_Then_LoadMessages_ShouldRoundTrip_InOrder()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var sessionId = "s1";

            var m1 = new ChatMessage(ChatRole.User, "hello");
            var m2 = new ChatMessage(ChatRole.Assistant, "hi");
            // 含 function call 的多态消息
            var m3 = new ChatMessage(ChatRole.Assistant, []);
            m3.Contents.Add(new FunctionCallContent("call_1", "foo", new Dictionary<string, object?> { ["x"] = 1 }));

            await storage.SaveMessage(sessionId, m1);
            await storage.SaveMessage(sessionId, m2);
            await storage.SaveMessage(sessionId, m3);

            var loaded = await storage.LoadMessages(sessionId);

            Assert.Equal(3, loaded.Count);
            Assert.Equal("hello", loaded[0].Text);
            Assert.Equal("hi", loaded[1].Text);
            var fc = loaded[2].Contents.OfType<FunctionCallContent>().Single();
            Assert.Equal("foo", fc.Name);
            Assert.Equal("call_1", fc.CallId);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task LoadMessages_UnknownSession_ReturnsEmpty()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var loaded = await storage.LoadMessages("nope");
            Assert.Empty(loaded);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task SaveSnapshot_Then_LoadSnapshot_RestoresState()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var snap = new AgentStateSnapshot
            {
                SessionId = "s1",
                AgentName = "TestAgent",
                SystemPrompt = "p",
                Items = new Dictionary<string, object> { ["k"] = "v" },
                SavedAt = DateTimeOffset.UtcNow,
                CheckpointReason = "ToolCallCompleted",
            };

            await storage.SaveSnapshotAsync("s1", snap);
            var loaded = await storage.LoadSnapshotAsync("s1");

            Assert.NotNull(loaded);
            Assert.Equal("TestAgent", loaded.AgentName);
            Assert.Equal("p", loaded.SystemPrompt);
            Assert.Equal("v", loaded.Items["k"].ToString());
            Assert.Equal("ToolCallCompleted", loaded.CheckpointReason);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task SaveSnapshot_OverwritesExisting()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            await storage.SaveSnapshotAsync("s1", new AgentStateSnapshot { SessionId = "s1", SystemPrompt = "first" });
            await storage.SaveSnapshotAsync("s1", new AgentStateSnapshot { SessionId = "s1", SystemPrompt = "second" });

            var loaded = await storage.LoadSnapshotAsync("s1");
            Assert.NotNull(loaded);
            Assert.Equal("second", loaded.SystemPrompt);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task LoadSnapshot_None_ReturnsNull()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            Assert.Null(await storage.LoadSnapshotAsync("missing"));
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task DeleteSnapshot_RemovesIt()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            await storage.SaveSnapshotAsync("s1", new AgentStateSnapshot { SessionId = "s1", SystemPrompt = "p" });
            await storage.DeleteSnapshotAsync("s1");
            Assert.Null(await storage.LoadSnapshotAsync("s1"));
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 规约 §8.1: 多线程并发 SaveMessage 不损坏。
    /// 5 个任务各写入 20 条消息(共 100 条)，全部完成后验证总数与反序列化正确性。
    /// </summary>
    [Fact]
    public async Task ConcurrentSaveMessage_WritesAllMessages_WithoutCorruption()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            const int taskCount = 5;
            const int messagesPerTask = 20;
            var sessionId = "concurrent-s1";

            var tasks = Enumerable.Range(0, taskCount).Select(taskIndex =>
            {
                return Task.Run(async () =>
                {
                    for (int i = 0; i < messagesPerTask; i++)
                    {
                        var msg = new ChatMessage(ChatRole.User, $"task{taskIndex}-msg{i}");
                        await storage.SaveMessage(sessionId, msg);
                    }
                });
            });

            await Task.WhenAll(tasks);

            var loaded = await storage.LoadMessages(sessionId);
            Assert.Equal(taskCount * messagesPerTask, loaded.Count);

            // 验证每条消息均可正常反序列化（无损坏）
            foreach (var m in loaded)
            {
                Assert.NotNull(m);
                Assert.False(string.IsNullOrEmpty(m.Text));
            }
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 规约 §Task3: SaveMessage 写消息后应更新对应 Sessions.LastAt。
    /// 会话行由 CreateNewSessionIdAsync 预建（正常路径）；此处走真实建会话路径，
    /// 连写两条消息后断言 Sessions.LastAt == 该会话消息 CreatedAt 的最大值。
    /// </summary>
    [Fact]
    public async Task SaveMessage_UpdatesSessionLastAt()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            // 真实建会话路径：CreateNewSessionIdAsync 预建 Sessions 行（含合规 UserId）
            var userStorage = new SqliteUserStorage(factory, NullLogger<SqliteUserStorage>.Instance);
            var sessionId = await userStorage.CreateNewSessionIdAsync("ext-1", SessionSource.Interactive);

            await storage.SaveMessage(sessionId, new ChatMessage(ChatRole.User, "hi"));
            await Task.Delay(50);
            await storage.SaveMessage(sessionId, new ChatMessage(ChatRole.User, "again"));

            await using var db = factory.CreateDbContext();
            var session = await db.Sessions.SingleAsync(x => x.SessionId == sessionId);
            var times = await db.SessionMessages
                .Where(x => x.SessionId == sessionId)
                .Select(x => x.CreatedAt)
                .ToListAsync();
            Assert.Equal(times.Max(), session.LastAt);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
