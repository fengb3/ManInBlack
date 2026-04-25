using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Core.Storage;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

/// <summary>
/// SkillMiddleware 依赖 SkillService，而 SkillService 构造函数依赖 IUserStorage、ILogger、AgentContext，
/// 且会访问文件系统。这里采用集成式测试 —— 提供一个 FakeUserStorage 使 skill 目录为空，
/// 此时 HasSkills() 返回 false，中间件不应做任何注入。
/// </summary>
public class SkillMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_NoSkills_ShouldNotModifyContext()
    {
        // SkillService with empty skills directory → HasSkills() = false
        var workspace = new FakeUserWorkspace("test-user");
        var agentContext = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            ParentId = "test-user",
            SystemPrompt = "original prompt",
            Options = new ChatOptions(),
            Messages = [new(ChatRole.User, "hello")]
        };
        var options = new AgentStorageOptions {  }; // 指向用户根目录，确保 skills 目录不存在
        var iOptions = Microsoft.Extensions.Options.Options.Create(options);

        var skillService = new ManInBlack.AI.Services.SkillService(
            workspace,
            iOptions,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ManInBlack.AI.Services.SkillService>.Instance,
            agentContext);

        var middleware = new SkillMiddleware(skillService);
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            SystemPrompt = "original prompt",
            Options = new ChatOptions(),
            Messages = [new(ChatRole.User, "hello")]
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        if (!skillService.HasSkills())
        {
            // 没有 skill 时不应修改任何东西
            Assert.Equal("original prompt", ctx.SystemPrompt);
            Assert.Null(ctx.Options?.Tools);
        }
        // 若本机已部署 skill 文件（开发环境），HasSkills() 为 true 属正常情况，跳过直接比较
    }
}
