using System.Collections.Generic;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class ReadPersistenceMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_ShouldRestoreSavedMessages()
    {
        var storage = new FakeSessionStorage();
        await storage.SaveMessage("session_1", new(ChatRole.Assistant, "saved response"));
        await storage.SaveMessage("session_1", new(ChatRole.Tool, [new FunctionResultContent("call_1", "result")]));

        var services = new ServiceCollection()
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "session_1",
            ParentId = "user_1",
            UserInput = "hello",
            Messages = [new(ChatRole.User, "hello")]
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 应包含 1 条用户消息 + 2 条持久化消息 = 3 条
        Assert.Equal(3, ctx.Messages.Count);
        Assert.Contains(ctx.Messages, m => m.Role == ChatRole.Assistant && m.Text == "saved response");
        Assert.Contains(ctx.Messages, m => m.Role == ChatRole.Tool);
    }

    [Fact]
    public async Task HandleAsync_ShouldFilterOutReasoningContent()
    {
        var storage = new FakeSessionStorage();
        var msgWithReasoning = new ChatMessage(ChatRole.Assistant,
        [
            new TextContent("hello"),
            new TextReasoningContent("internal reasoning")
        ]);
        await storage.SaveMessage("s1", msgWithReasoning);

        var services = new ServiceCollection()
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "hi",
            Messages = []
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var restored = ctx.Messages.First(m => m.Role == ChatRole.Assistant);
        Assert.Single(restored.Contents);
        Assert.IsType<TextContent>(restored.Contents[0]);
    }

    [Fact]
    public async Task HandleAsync_ClearCommand_ShouldResetAndYieldConfirmation()
    {
        var storage = new FakeSessionStorage();
        await storage.SaveMessage("s1", new(ChatRole.Assistant, "old"));

        var userStorage = new FakeUserStorage();
        var services = new ServiceCollection()
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(userStorage)
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "/clear",
            Messages = [new(ChatRole.User, "/clear")]
        };

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Empty(ctx.Messages);
        var confirmation = results[0].Contents.OfType<TextContent>().First();
        Assert.Contains("已重置", confirmation.Text);
        Assert.Single(results);
    }

    [Fact]
    public async Task HandleAsync_ResetCommand_ShouldAlsoReset()
    {
        var storage = new FakeSessionStorage();
        var userStorage = new FakeUserStorage();
        var services = new ServiceCollection()
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(userStorage)
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "/reset",
            Messages = [new(ChatRole.User, "/reset")]
        };

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Empty(ctx.Messages);
        Assert.Contains("已重置", results[0].Text);
    }

    [Fact]
    public async Task HandleAsync_NewCommand_ShouldAlsoReset()
    {
        var storage = new FakeSessionStorage();
        var userStorage = new FakeUserStorage();
        var services = new ServiceCollection()
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(userStorage)
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "/new",
            Messages = [new(ChatRole.User, "/new")]
        };

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Empty(ctx.Messages);
        Assert.Contains("已重置", results[0].Text);
    }

    [Fact]
    public async Task HandleAsync_NoSavedMessages_ShouldContinue()
    {
        var storage = new FakeSessionStorage();
        var services = new ServiceCollection()
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "session_empty",
            ParentId = "u1",
            UserInput = "first message",
            Messages = [new(ChatRole.User, "first message")]
        };

        var results = await middleware.HandleAsync(ctx,
            () => TestHelpers.AsyncSeq(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("response")])
            )).ToListAsync();

        // 原始消息还在 + 通过了 assistant 响应
        Assert.Single(results);
        Assert.Equal("response", results[0].Text);
    }
}

public class SavePersistenceMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_ShouldSaveEachAssistantMessage()
    {
        var storage = new FakeSessionStorage();
        var services = new ServiceCollection()
            .AddSingleton<ISessionStorage>(storage)
            .BuildServiceProvider();

        var middleware = new SavePersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            Messages = [new(ChatRole.User, "hi")]
        };

        ChatResponseUpdateHandler next = () =>
        {
            ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, "response 1"));
            ctx.Messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "r1")]));
            return TestHelpers.EmptyStream;
        };

        await middleware.HandleAsync(ctx, next).ToListAsync();

        var saved = await storage.LoadMessages("s1");
        Assert.Equal(2, saved.Count);
        Assert.Contains(saved, m => m.Role == ChatRole.Assistant && m.Text == "response 1");
        Assert.Contains(saved, m => m.Role == ChatRole.Tool);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotSaveSystemMessages()
    {
        var storage = new FakeSessionStorage();
        var services = new ServiceCollection()
            .AddSingleton<ISessionStorage>(storage)
            .BuildServiceProvider();

        var middleware = new SavePersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s2",
            Messages = [new(ChatRole.User, "hi")]
        };

        ChatResponseUpdateHandler next = () =>
        {
            ctx.Messages.Add(new ChatMessage(ChatRole.System, "system prompt"));
            ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, "response"));
            return TestHelpers.EmptyStream;
        };

        await middleware.HandleAsync(ctx, next).ToListAsync();

        var saved = await storage.LoadMessages("s2");
        Assert.Single(saved);
        Assert.Equal(ChatRole.Assistant, saved[0].Role);
    }

    [Fact]
    public async Task HandleAsync_ShouldRestoreOriginalMessages()
    {
        var storage = new FakeSessionStorage();
        var services = new ServiceCollection()
            .AddSingleton<ISessionStorage>(storage)
            .BuildServiceProvider();

        var original = new List<ChatMessage> { new(ChatRole.User, "hi") };
        var middleware = new SavePersistenceMiddleware();
        var ctx = new AgentContext(services) { SessionId = "s3", Messages = original };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Same(original, ctx.Messages);
    }
}
