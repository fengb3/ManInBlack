using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        var storage = new FakeAgentStateStorage();
        await storage.SaveMessage("session_1", new(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "Tool", null)]));
        await storage.SaveMessage("session_1", new(ChatRole.Tool, [new FunctionResultContent("call_1", "result")]));

        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
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
        Assert.Contains(ctx.Messages, m => m.Role == ChatRole.Assistant
            && m.Contents.OfType<FunctionCallContent>().Any(c => c.CallId == "call_1"));
        Assert.Contains(ctx.Messages, m => m.Role == ChatRole.Tool);
    }

    // this one does not need anymore, deepseek (maybe other models) require sending back reasoning content
    // [Fact]
    // public async Task HandleAsync_ShouldFilterOutReasoningContent()
    // {
    //     var storage = new FakeAgentStateStorage();
    //     var msgWithReasoning = new ChatMessage(ChatRole.Assistant,
    //     [
    //         new TextContent("hello"),
    //         new TextReasoningContent("internal reasoning")
    //     ]);
    //     await storage.SaveMessage("s1", msgWithReasoning);
    //
    //     var services = new ServiceCollection()
    //         .AddSingleton<ISessionStorage>(storage)
    //         .AddSingleton<IUserStorage>(new FakeUserStorage())
    //         .BuildServiceProvider();
    //
    //     var middleware = new ReadPersistenceMiddleware();
    //     var ctx = new AgentContext(services)
    //     {
    //         SessionId = "s1",
    //         ParentId = "u1",
    //         UserInput = "hi",
    //         Messages = []
    //     };
    //
    //     await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();
    //
    //     var restored = ctx.Messages.First(m => m.Role == ChatRole.Assistant);
    //     Assert.Single(restored.Contents);
    //     Assert.IsType<TextContent>(restored.Contents[0]);
    // }

    [Fact]
    public async Task HandleAsync_NoSavedMessages_ShouldContinue()
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

    /// <summary>
    /// 构造 ReadPersistenceMiddleware + 空 Messages 的 AgentContext，便于孤儿修复测试。
    /// </summary>
    private static (ReadPersistenceMiddleware middleware, AgentContext ctx) BuildReadMiddleware(
        FakeAgentStateStorage storage, string sessionId)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();
        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = sessionId,
            ParentId = "u1",
            Messages = []
        };
        return (middleware, ctx);
    }

    private static List<FunctionResultContent> AllToolResults(IEnumerable<ChatMessage> messages) =>
        messages.Where(m => m.Role == ChatRole.Tool)
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();

    [Fact]
    public async Task HandleAsync_TrailingOrphanToolCalls_HealsWithStub()
    {
        var storage = new FakeAgentStateStorage();
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "do it"));
        // 孤儿：assistant(tool_calls) 没有对应的 tool 结果（模拟打断后残留）
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("c1", "ToolA", null)]));

        var (middleware, ctx) = BuildReadMiddleware(storage, "s1");
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var results = AllToolResults(ctx.Messages);
        var healed = results.FirstOrDefault(r => r.CallId == "c1");
        Assert.NotNull(healed);
        Assert.Contains("中断", healed!.Result?.ToString() ?? "");
    }

    [Fact]
    public async Task HandleAsync_MidHistoryOrphan_HealsBeforeNextAssistant()
    {
        var storage = new FakeAgentStateStorage();
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "x"));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "A", null)]));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c2", "B", null)]));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c2", "r2")]));

        var (middleware, ctx) = BuildReadMiddleware(storage, "s1");
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // c1 孤儿被补桩
        var idxC1Stub = ctx.Messages.IndexOf(ctx.Messages.First(m =>
            m.Role == ChatRole.Tool && m.Contents.OfType<FunctionResultContent>().Any(r => r.CallId == "c1")));
        // c2 真实结果保留
        var idxC2Result = ctx.Messages.IndexOf(ctx.Messages.First(m =>
            m.Role == ChatRole.Tool && m.Contents.OfType<FunctionResultContent>().Any(r => r.CallId == "c2")));

        Assert.True(idxC1Stub >= 0 && idxC2Result >= 0);
        Assert.True(idxC1Stub < idxC2Result, "c1 桩应出现在 c2 结果之前");
        Assert.Equal("r2", ctx.Messages[idxC2Result].Contents.OfType<FunctionResultContent>()
            .First(r => r.CallId == "c2").Result?.ToString());
    }

    [Fact]
    public async Task HandleAsync_AssistantToolCallsFollowedByUser_HealsBeforeUser()
    {
        var storage = new FakeAgentStateStorage();
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "first"));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "A", null)]));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "interrupted"));

        var (middleware, ctx) = BuildReadMiddleware(storage, "s1");
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // user("interrupted") 之前应有 c1 的桩
        var idxUser = ctx.Messages.IndexOf(ctx.Messages.Last(m => m.Role == ChatRole.User && m.Text == "interrupted"));
        var idxStub = ctx.Messages.IndexOf(ctx.Messages.First(m =>
            m.Role == ChatRole.Tool && m.Contents.OfType<FunctionResultContent>().Any(r => r.CallId == "c1")));
        Assert.True(idxStub >= 0 && idxStub < idxUser);
    }

    [Fact]
    public async Task HandleAsync_CompleteHistory_IsNoOp()
    {
        var storage = new FakeAgentStateStorage();
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "x"));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "A", null)]));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "r1")]));

        var (middleware, ctx) = BuildReadMiddleware(storage, "s1");
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 已健全的历史不应插入额外桩：只有 1 条 tool 消息（c1 的真实结果）
        var toolMessages = ctx.Messages.Where(m => m.Role == ChatRole.Tool).ToList();
        Assert.Single(toolMessages);
        Assert.Equal("r1", toolMessages[0].Contents.OfType<FunctionResultContent>().Single().Result?.ToString());
    }

    [Fact]
    public async Task HandleAsync_MultipleCallsOneMessage_AllAnswered_IsNoOp()
    {
        var storage = new FakeAgentStateStorage();
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "x"));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("c1", "A", null),
            new FunctionCallContent("c2", "B", null)
        ]));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("c1", "r1"),
            new FunctionResultContent("c2", "r2")
        ]));

        var (middleware, ctx) = BuildReadMiddleware(storage, "s1");
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var toolMessages = ctx.Messages.Where(m => m.Role == ChatRole.Tool).ToList();
        Assert.Single(toolMessages); // 不补桩
        var ids = toolMessages[0].Contents.OfType<FunctionResultContent>().Select(r => r.CallId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "c1", "c2" }, ids);
    }

    [Fact]
    public async Task HandleAsync_MultipleCallsOneMessage_PartiallyAnswered_HealsMissing()
    {
        var storage = new FakeAgentStateStorage();
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "x"));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("c1", "A", null),
            new FunctionCallContent("c2", "B", null)
        ]));
        // 只有 c1 有结果，c2 是孤儿
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "r1")]));

        var (middleware, ctx) = BuildReadMiddleware(storage, "s1");
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var results = AllToolResults(ctx.Messages);
        Assert.Contains(results, r => r.CallId == "c1" && r.Result?.ToString() == "r1");
        var c2 = results.FirstOrDefault(r => r.CallId == "c2");
        Assert.NotNull(c2);
        Assert.Contains("中断", c2!.Result?.ToString() ?? "");
    }

    [Fact]
    public async Task HandleAsync_InterleavedOrphanToolResult_IsDroppedAndToolCallsStubbed()
    {
        // 复现并发持久化交错：assistant(tool_calls c1) 之后被 user/assistant 隔断，
        // 被打断那一轮的 tool 结果姗姗来迟，落到 user/assistant 之后——
        // 这条 tool 结果的前一条不是 tool_calls，会被 API 拒绝（"tool must be a response to preceding tool_calls"）。
        var storage = new FakeAgentStateStorage();
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "msg1"));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "A", null)]));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.User, "interrupt"));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Assistant, "partial text"));
        await storage.SaveMessage("s1", new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "late result")]));

        var (middleware, ctx) = BuildReadMiddleware(storage, "s1");
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 交错的孤儿 tool 结果（"late result"）应被丢弃
        var results = AllToolResults(ctx.Messages);
        Assert.DoesNotContain(results, r => r.Result?.ToString() == "late result");

        // assistant(tool_calls c1) 紧后应补一条桩结果，保证配对
        var idxAsst = ctx.Messages.IndexOf(ctx.Messages.First(m =>
            m.Role == ChatRole.Assistant && m.Contents.OfType<FunctionCallContent>().Any(c => c.CallId == "c1")));
        var toolAfter = ctx.Messages[idxAsst + 1];
        Assert.Equal(ChatRole.Tool, toolAfter.Role);
        Assert.Equal("c1", toolAfter.Contents.OfType<FunctionResultContent>().Single().CallId);
    }
}

public class SavePersistenceMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_ShouldSaveEachAssistantMessage()
    {
        var storage = new FakeAgentStateStorage();
        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
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
        var storage = new FakeAgentStateStorage();
        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
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
        var storage = new FakeAgentStateStorage();
        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
            .AddSingleton<ISessionStorage>(storage)
            .BuildServiceProvider();

        var original = new List<ChatMessage> { new(ChatRole.User, "hi") };
        var middleware = new SavePersistenceMiddleware();
        var ctx = new AgentContext(services) { SessionId = "s3", Messages = original };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Same(original, ctx.Messages);
    }

    [Fact]
    public async Task HandleAsync_CancelledTurn_DoesNotPersistPostCancelMessages()
    {
        // 被打断的那一轮，取消令牌触发后再追加的消息不应落库——
        // 否则它会和随后启动的新一轮并发写同一会话，产生交错污染（孤儿 tool 结果等）。
        var storage = new FakeAgentStateStorage();
        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
            .AddSingleton<ISessionStorage>(storage)
            .BuildServiceProvider();

        var middleware = new SavePersistenceMiddleware();
        var ctx = new AgentContext(services) { SessionId = "s4", Messages = [] };
        var cts = new CancellationTokenSource();

        ChatResponseUpdateHandler next = () =>
        {
            ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, "before cancel")); // 取消前：应落库
            cts.Cancel();                                                             // 模拟打断
            ctx.Messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "after cancel")])); // 取消后：不应落库
            return TestHelpers.EmptyStream;
        };

        await middleware.HandleAsync(ctx, next, cts.Token).ToListAsync();

        var saved = await storage.LoadMessages("s4");
        Assert.Contains(saved, m => m.Role == ChatRole.Assistant && m.Text == "before cancel");
        Assert.DoesNotContain(saved, m => m.Role == ChatRole.Tool);
    }
}
