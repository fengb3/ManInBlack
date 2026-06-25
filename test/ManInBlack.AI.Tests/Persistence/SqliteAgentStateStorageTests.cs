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
}
