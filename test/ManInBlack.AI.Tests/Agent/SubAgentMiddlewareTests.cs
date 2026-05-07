using System.Linq;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Agent;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Agent;

public class SubAgentMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_WithAgents_InjectsDescriptionIntoPrompt()
    {
        var defs = new[]
        {
            new AgentDefinition { Name = "coder", Description = "编写代码" },
            new AgentDefinition { Name = "analyst", Description = "分析文件" },
        };
        var registry = new AgentRegistry(defs);
        var middleware = new SubAgentMiddleware(registry);

        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            SystemPrompt = "原始提示词",
            Options = new ChatOptions(),
            Messages = [new(ChatRole.User, "hello")]
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Contains("coder", ctx.SystemPrompt);
        Assert.Contains("analyst", ctx.SystemPrompt);
        Assert.Contains("编写代码", ctx.SystemPrompt);
        Assert.Contains("可用的 Sub-Agent", ctx.SystemPrompt);
    }

    [Fact]
    public async Task HandleAsync_WithNoAgents_DoesNotModifyPrompt()
    {
        var registry = new AgentRegistry([]);
        var middleware = new SubAgentMiddleware(registry);

        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            SystemPrompt = "原始提示词",
            Options = new ChatOptions(),
            Messages = [new(ChatRole.User, "hello")]
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Equal("原始提示词", ctx.SystemPrompt);
    }
}
