using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class CheckpointTests
{
    /// <summary>
    /// 快照保存后加载，Items 和 SystemPrompt 应一致
    /// </summary>
    [Fact]
    public async Task SaveAndLoadSnapshot_ShouldRestoreState()
    {
        var storage = new FakeAgentStateStorage();
        var snapshot = new AgentStateSnapshot
        {
            SessionId = "s1",
            AgentName = "TestAgent",
            SystemPrompt = "test prompt",
            Items = new Dictionary<string, object> { ["key1"] = "value1" },
            SavedAt = DateTimeOffset.UtcNow,
            CheckpointReason = "ToolCallCompleted"
        };

        await storage.SaveSnapshotAsync("s1", snapshot);
        var loaded = await storage.LoadSnapshotAsync("s1");

        Assert.NotNull(loaded);
        Assert.Equal("s1", loaded.SessionId);
        Assert.Equal("TestAgent", loaded.AgentName);
        Assert.Equal("test prompt", loaded.SystemPrompt);
        Assert.Equal("value1", loaded.Items["key1"]);
        Assert.Equal("ToolCallCompleted", loaded.CheckpointReason);
    }

    /// <summary>
    /// 无快照时应返回 null
    /// </summary>
    [Fact]
    public async Task LoadSnapshot_NoSnapshot_ShouldReturnNull()
    {
        var storage = new FakeAgentStateStorage();
        var result = await storage.LoadSnapshotAsync("nonexistent");
        Assert.Null(result);
    }

    /// <summary>
    /// ReadPersistenceMiddleware 恢复快照时还原 Items 和 SystemPrompt
    /// </summary>
    [Fact]
    public async Task ReadPersistence_ShouldRestoreSnapshot()
    {
        var storage = new FakeAgentStateStorage();
        await storage.SaveSnapshotAsync("s1", new AgentStateSnapshot
        {
            SessionId = "s1",
            AgentName = "Agent",
            SystemPrompt = "restored prompt",
            Items = new Dictionary<string, object> { ["myKey"] = "myValue" },
        });

        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "hello",
            SystemPrompt = "original prompt",
            Messages = [new(ChatRole.User, "hello")]
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // SystemPrompt 不从快照恢复（由 AgentFactory + SystemPromptInjectionMiddleware 重新生成）
        Assert.Equal("original prompt", ctx.SystemPrompt);
        Assert.Equal("myValue", ctx.Items["myKey"]);
    }

    /// <summary>
    /// Items 中的不可序列化值在保存时被跳过
    /// </summary>
    [Fact]
    public async Task SaveCheckpoint_ShouldSkipNonSerializableItems()
    {
        var storage = new FakeAgentStateStorage();
        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "hello",
            Messages = [new(ChatRole.User, "hello")]
        };

        // 执行中间件以注入 SaveCheckpoint 回调
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 添加不可序列化对象
        ctx.Items["func"] = (Func<int>)(() => 42);
        ctx.Items["valid"] = "hello";

        // 手动触发保存
        if (ctx.Items.TryGetValue("SaveCheckpoint", out var obj) && obj is Func<string?, CancellationToken, Task> save)
            await save("SessionEnd", CancellationToken.None);

        var snapshot = await storage.LoadSnapshotAsync("s1");
        Assert.NotNull(snapshot);
        Assert.False(snapshot.Items.ContainsKey("func"));
        Assert.False(snapshot.Items.ContainsKey("SaveCheckpoint"));
        Assert.Equal("hello", snapshot.Items["valid"]);
    }

    /// <summary>
    /// SavePersistenceMiddleware 结束时应触发 SessionEnd 检查点
    /// </summary>
    [Fact]
    public async Task SavePersistence_ShouldTriggerSessionEndCheckpoint()
    {
        var storage = new FakeAgentStateStorage();
        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();

        // 先用 ReadPersistenceMiddleware 注入 SaveCheckpoint 回调
        var readMiddleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "hello",
            SystemPrompt = "test prompt",
            Messages = [new(ChatRole.User, "hello")]
        };

        await readMiddleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 再用 SavePersistenceMiddleware 执行
        var saveMiddleware = new SavePersistenceMiddleware();
        ChatResponseUpdateHandler next = () =>
        {
            ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, "response"));
            return TestHelpers.EmptyStream;
        };

        await saveMiddleware.HandleAsync(ctx, next).ToListAsync();

        var snapshot = await storage.LoadSnapshotAsync("s1");
        Assert.NotNull(snapshot);
        Assert.Equal("SessionEnd", snapshot.CheckpointReason);
    }

}
