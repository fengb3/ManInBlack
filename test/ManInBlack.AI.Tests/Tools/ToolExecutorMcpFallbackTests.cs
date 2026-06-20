using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Mcp;
using ManInBlack.AI.ToolCallFilters;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ManInBlack.AI.Tests.Tools;

/// <summary>
/// 验证 ToolExecutor 在静态 handler 字典 miss 时 fallback 到 IMcpToolProvider，
/// 以及结果回填 / 异常路径。AgentLifecycleFilter 事件链（飞书卡片）由其自身的测试覆盖，
/// 这里用空 ServiceProvider（GetService 返回 null）走 core 直执行路径。
/// </summary>
public class ToolExecutorMcpFallbackTests
{
    private static IServiceProvider EmptySp => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task ExecuteAsync_Mcp工具_走ProviderFallback并写回Result()
    {
        var provider = new FakeMcpProvider("srv__search", "搜索结果文本");
        var executor = new ToolExecutor([], provider);
        var ctx = new ToolExecuteContext(EmptySp) { ToolName = "srv__search", CallId = "c1" };

        await executor.ExecuteAsync(ctx, default);

        Assert.Null(ctx.Error);
        Assert.Equal("搜索结果文本", ctx.Result);
    }

    [Fact]
    public async Task ExecuteAsync_未知工具_设置Error()
    {
        var executor = new ToolExecutor([], null);
        var ctx = new ToolExecuteContext(EmptySp) { ToolName = "unknown_tool" };

        await executor.ExecuteAsync(ctx, default);

        Assert.NotNull(ctx.Error);
        Assert.Contains("Unknown tool", ctx.Error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Provider抛McpToolException_设置Error()
    {
        var provider = new FakeMcpProvider("srv__fail", throwOnExec: true);
        var executor = new ToolExecutor([], provider);
        var ctx = new ToolExecuteContext(EmptySp) { ToolName = "srv__fail" };

        await executor.ExecuteAsync(ctx, default);

        Assert.NotNull(ctx.Error);
        Assert.IsType<McpToolException>(ctx.Error);
    }

    [Fact]
    public async Task ExecuteAsync_Mcp工具_Filter链不递归()
    {
        // 回归测试：filter 链包装必须用局部变量捕获 pipeline 快照，
        // 否则闭包捕获被重新赋值的变量会无限递归导致 stack overflow
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<LoggingFilter>();
        var sp = services.BuildServiceProvider();

        var provider = new FakeMcpProvider("srv__t", "ok");
        var executor = new ToolExecutor([], provider);
        var ctx = new ToolExecuteContext(sp)
        {
            ToolName = "srv__t",
            CallId = "c1",
            Arguments = new Dictionary<string, object?>()
        };

        await executor.ExecuteAsync(ctx, default);

        Assert.Null(ctx.Error);
        Assert.Equal("ok", ctx.Result);
    }

    private sealed class FakeMcpProvider : IMcpToolProvider
    {
        private readonly string _knownTool;
        private readonly string _result;
        private readonly bool _throw;

        public FakeMcpProvider(string knownTool, string result = "", bool throwOnExec = false)
        {
            _knownTool = knownTool;
            _result = result;
            _throw = throwOnExec;
        }

        public bool IsMcpTool(string fullyQualifiedName) => fullyQualifiedName == _knownTool;

        public Task<string> ExecuteAsync(string fullyQualifiedName, IDictionary<string, object?>? arguments, CancellationToken cancellationToken)
            => _throw ? throw new McpToolException("boom") : Task.FromResult(_result);
    }
}
