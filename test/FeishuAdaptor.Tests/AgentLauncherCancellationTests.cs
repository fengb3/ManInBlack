using System.Runtime.CompilerServices;
using FeishuAdaptor.EventHandlers;
using FeishuNetSdk.Core;
using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Mcp;
using ManInBlack.AI.Services;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// 回归测试:飞书「打断」机制。
/// 修复前(ImMessageReceiveEventHandler.cs):<c>factory.RegisterAndCancelExisting(userId)</c>
/// 返回的 CTS 仅用于文件下载阶段,真正的 <c>factory.RunAsync(...)</c> 调用漏传了
/// <c>cts.Token</c>,导致 <see cref="AgentContext.CancellationToken"/> 全程为 None,
/// 第二条消息无法取消正在运行的旧 Agent。
/// </summary>
public class AgentLauncherCancellationTests
{
    [Fact]
    public async Task LaunchAsync_新消息到达_取消旧Agent注册的CancellationToken()
    {
        var holder = new TokenHolder();

        var services = new ServiceCollection();
        services.AddSingleton(holder);                  // CapturingMiddleware 通过 DI 拿到同一个 holder
        services.AddScoped<CapturingMiddleware>();
        services.AddScoped<AgentContext>();             // ServiceProvider 由 DI 自动注入
        services.AddSingleton<IUserStorage>(new FakeUserStorage());
        services.AddSingleton<EventBus>();              // Launcher 的 configure 回调要解析它
        services.AddSingleton<IChatClient>(_ => Substitute.For<IChatClient>());      // CapturingMiddleware 不调 next,永不触达
        services.AddSingleton<IHttpClientFactory>(_ => Substitute.For<IHttpClientFactory>());
        services.AddSingleton<McpClientHostedService>(_ => new McpClientHostedService(
            Options.Create(new ManInBlackSettings()),   // McpServers 默认空 → EnsureStartedAsync 立即完成
            new ToolRegistry([]),
            NullLoggerFactory.Instance,
            NullLogger<McpClientHostedService>.Instance));

        var rootSp = services.BuildServiceProvider();
        var scopeFactory = rootSp.GetRequiredService<IServiceScopeFactory>();

        var feishuDef = new AgentDefinition
        {
            Name = "feishu-agent",
            Instruction = "test",
            PipelineName = "capture",
        };
        var factory = new AgentFactory(scopeFactory, NullLogger<AgentFactory>.Instance, [feishuDef], []);
        factory.RegisterPipeline("capture", b => b.Use<CapturingMiddleware>());

        var launcher = new AgentLauncher(rootSp, factory, NullLogger<AgentLauncher>.Instance);

        // 构造一条 p2p 文本消息(eventId 必须唯一,否则被 ImMessageReceiveEventHandler 的去重表跳过)
        var dto = new EventV2Dto<ImMessageReceiveV1EventBodyDto>
        {
            EventId = Guid.NewGuid().ToString(),
            Event = new ImMessageReceiveV1EventBodyDto
            {
                Sender = new ImMessageReceiveV1EventBodyDto.EventSender
                {
                    SenderId = new UserIdSuffix { UserId = "u1", OpenId = "o1" },
                },
                Message = new ImMessageReceiveV1EventBodyDto.EventMessage
                {
                    ChatType = "p2p",
                    MessageType = "text",
                    Content = "{\"text\":\"第一条消息\"}",
                },
            },
        };

        // 启动第一条消息:运行到 CapturingMiddleware 后阻塞在 Gate 上,保持旧 Agent 处于在途状态
        // (在途才能保证 _tracking[u1] 仍指向旧 CTS,随后被第二条消息取消)。
        var launchTask = launcher.LaunchAsync(dto);
        try
        {
            await holder.Ready.Task;                       // 等到管道捕获到旧 Agent 的 CancellationToken
            factory.RegisterAndCancelExisting("u1");       // 模拟第二条消息到达:取消旧 Agent、注册新 CTS

            // 旧 Agent 管道里捕获到的 token 必须被取消。
            // 修复前:RunAsync 收到 None → holder.Captured 是 None → 此处 false(测试失败)。
            // 修复后:RunAsync 收到 cts.Token → 被第二条消息取消 → 此处 true。
            Assert.True(
                holder.Captured.IsCancellationRequested,
                "第二条消息必须取消第一条消息注册的 CancellationToken(打断机制)");
        }
        finally
        {
            holder.Gate.SetResult();                       // 放行阻塞的管道,让 launchTask 干净收尾
            try { await launchTask; }
            catch { /* 清理阶段忽略异常,不掩盖断言失败 */ }
        }
    }

    /// <summary>捕获到达管道的 <see cref="AgentContext.CancellationToken"/>,并阻塞管道让旧 Agent 保持在途。</summary>
    public sealed class TokenHolder
    {
        public CancellationToken Captured;
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// 最内层中间件:捕获 <see cref="AgentContext.CancellationToken"/>,通知测试,然后阻塞在
    /// <see cref="TokenHolder.Gate"/> 上(不调 next,故 IChatClient 永不触达)。
    /// </summary>
    public sealed class CapturingMiddleware(TokenHolder holder) : AgentMiddleware
    {
        public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
            AgentContext context,
            ChatResponseUpdateHandler next,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            holder.Captured = context.CancellationToken;
            holder.Ready.TrySetResult();
            await holder.Gate.Task;
            yield break;
        }
    }

    /// <summary>内存版 IUserStorage:任意 userId 返回带会话的用户。</summary>
    private sealed class FakeUserStorage : IUserStorage
    {
        public Task<UserEntry> GetOrCreateUser(string userId)
            => Task.FromResult(new UserEntry { UserId = userId, SelfHostUserId = "1" });

        public Task SaveUserAsync(UserEntry userEntry) => Task.CompletedTask;

        public Task<string> CreateNewSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
            => Task.FromResult("s-new");

        public Task<string?> GetLatestSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
            => Task.FromResult<string?>("s-latest");
    }
}
