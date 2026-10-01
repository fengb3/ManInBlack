using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Storage;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Storage;

public class InMemoryStorageTests
{
    [Fact]
    public async Task InMemoryUserStorage_CreateNewSessionId_TwiceInSameSecond_DoesNotCollide()
    {
        var storage = new InMemoryUserStorage();
        var sid1 = await storage.CreateNewSessionIdAsync("u1");
        var sid2 = await storage.CreateNewSessionIdAsync("u1");

        Assert.NotEqual(sid1, sid2);
        Assert.StartsWith("u1_", sid1);
    }

    [Fact]
    public async Task InMemoryUserStorage_GetLatestSessionId_ReturnsLatestBySource()
    {
        var storage = new InMemoryUserStorage();
        await storage.CreateNewSessionIdAsync("u1", SessionSource.Webhook);
        var interactive = await storage.CreateNewSessionIdAsync("u1", SessionSource.Interactive);

        var latest = await storage.GetLatestSessionIdAsync("u1", SessionSource.Interactive);
        Assert.Equal(interactive, latest);
    }

    [Fact]
    public async Task InMemoryAgentStateStorage_SaveMessage_LoadMessages_RoundTrip()
    {
        var storage = new InMemoryAgentStateStorage();
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "hello"));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant, "hi"));

        var loaded = await storage.LoadMessages("s1");
        Assert.Equal(2, loaded.Count);
        Assert.Equal("hello", loaded[0].Text);
    }

    [Fact]
    public async Task InMemoryAgentStateStorage_Snapshot_RoundTrip()
    {
        var storage = new InMemoryAgentStateStorage();
        var snap = new AgentStateSnapshot
        {
            SessionId = "s1",
            SystemPrompt = "p",
            Items = new Dictionary<string, object> { ["k"] = "v" },
            SavedAt = DateTimeOffset.UtcNow,
        };

        await storage.SaveSnapshotAsync("s1", snap);
        var loaded = await storage.LoadSnapshotAsync("s1");

        Assert.NotNull(loaded);
        Assert.Equal("p", loaded.SystemPrompt);
        Assert.Equal("v", loaded.Items["k"].ToString());
    }
}
