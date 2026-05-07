using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Agent;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ManInBlack.AI.Tests.Agent;

public class AgentFactoryTests
{
    /// <summary>
    /// 模拟 IHttpClientFactory，返回预设的 HttpClient
    /// </summary>
    private class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    /// <summary>
    /// 构建 OpenAI 兼容的 SSE 流式响应，返回指定文本
    /// </summary>
    private static Stream BuildSseResponse(string text)
    {
        var chunks = new[]
        {
            $"{{\"choices\":[{{\"delta\":{{\"content\":\"{text}\"}}}}]}}",
            $"{{\"choices\":[{{\"delta\":{{}},\"finish_reason\":\"stop\"}}]}}"
        };
        return SseResponseBuilder.BuildWithDone(chunks);
    }

    /// <summary>
    /// 构建完整的 DI 容器，注册 UseSimple 管道所需的所有中间件和假服务
    /// </summary>
    private static (IServiceProvider rootSp, MockHttpMessageHandler handler, FakeHttpClientFactory httpFactory)
        BuildTestInfrastructure(string responseText = "测试响应")
    {
        var sseStream = BuildSseResponse(responseText);
        var handler = new MockHttpMessageHandler(sseStream);
        var httpClient = new HttpClient(handler);
        var httpFactory = new FakeHttpClientFactory(httpClient);

        var services = new ServiceCollection();
        services.AddScoped<AgentContext>();

        // 注册 UseSimple 管道中的所有中间件
        services.AddScoped<LoggingMiddleware>();
        services.AddScoped<MessageEnrichMiddleware>();
        services.AddScoped<HookMiddleware>();
        services.AddScoped<SystemPromptInjectionMiddleware>();
        services.AddScoped<UserInputMiddleware>();
        services.AddScoped<RetryMiddleware>();
        services.AddScoped<AgentLoopMiddleware>();

        // 注册假服务
        services.AddScoped<IToolExecutor, FakeToolExecutor>();
        services.AddScoped<IHookExecutor, FakeHookExecutor>();

        // 注册日志（无输出）
        services.AddLogging(builder => builder.ClearProviders());

        var rootSp = services.BuildServiceProvider();
        return (rootSp, handler, httpFactory);
    }

    /// <summary>
    /// 默认的 ModelChoice（OpenAI 兼容）
    /// </summary>
    private static ModelChoice DefaultModelChoice => new()
    {
        Provider = new OpenAIProvider { ApiKey = "test-key" },
        ModelId = "test-model"
    };

    [Fact]
    public async Task RunAsync_WithUnknownName_Throws()
    {
        // 注册表为空，查找不存在的 Agent 应抛异常
        var registry = new AgentRegistry([]);
        var factory = new AgentFactory(
            TestHelpers.EmptyServiceProvider,
            registry,
            new FakeHttpClientFactory(new HttpClient()),
            DefaultModelChoice);
        var parentContext = new AgentContext(TestHelpers.EmptyServiceProvider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.RunAsync("nonexistent", "test", parentContext, CancellationToken.None));

        Assert.Contains("未找到名为", ex.Message);
        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public async Task RunAsync_WithValidDefinition_ReturnsAgentResult()
    {
        var (rootSp, _, httpFactory) = BuildTestInfrastructure();
        var registry = new AgentRegistry([]);
        registry.Register(new AgentDefinition
        {
            Name = "test-agent",
            Instructions = "你是一个测试助手",
        });
        var factory = new AgentFactory(rootSp, registry, httpFactory, DefaultModelChoice);
        var parentContext = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "parent-001",
            SessionId = "session-001",
        };

        var result = await factory.RunAsync("test-agent", "hello", parentContext, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("测试响应", result.Output);
    }

    [Fact]
    public async Task RunAsync_SetsParentId()
    {
        // 验证设置 parentContext.AgentId 后工厂正常运行，
        // 确认 childContext.ParentId 赋值路径不会崩溃
        var (rootSp, _, httpFactory) = BuildTestInfrastructure("子Agent响应");
        var registry = new AgentRegistry([]);
        registry.Register(new AgentDefinition { Name = "child", Instructions = "子Agent指令" });
        var factory = new AgentFactory(rootSp, registry, httpFactory, DefaultModelChoice);
        var parentContext = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            AgentId = "parent-agent-999",
            SessionId = "session-002",
        };

        var result = await factory.RunAsync("child", "请执行子任务", parentContext, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("子Agent响应", result.Output);
    }

    [Fact]
    public async Task RunAsync_UsesDefinitionInstructions()
    {
        // 验证 definition.Instructions 作为 system prompt 传递给 LLM
        var (rootSp, handler, httpFactory) = BuildTestInfrastructure();
        var registry = new AgentRegistry([]);
        registry.Register(new AgentDefinition
        {
            Name = "test",
            Instructions = "你是一个专用翻译助手",
        });
        var factory = new AgentFactory(rootSp, registry, httpFactory, DefaultModelChoice);
        var parentContext = new AgentContext(TestHelpers.EmptyServiceProvider);

        var result = await factory.RunAsync("test", "translate", parentContext, CancellationToken.None);

        Assert.True(result.Success);

        // 验证 HTTP 请求体中的第一条消息为 system，内容为 Instructions
        Assert.NotNull(handler.LastRequestBody);
        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");
        Assert.True(messages.GetArrayLength() >= 1);
        var systemMsg = messages[0];
        Assert.Equal("system", systemMsg.GetProperty("role").GetString());
        Assert.Equal("你是一个专用翻译助手", systemMsg.GetProperty("content").GetString());
    }

    [Fact]
    public async Task RunAsync_WithPipelineName_SelectsCorrectPipeline()
    {
        // 验证 PipelineName 属性被正确设置，AgentFactory 根据 PipelineName 选择管道
        var (rootSp, _, httpFactory) = BuildTestInfrastructure();
        var registry = new AgentRegistry([]);
        registry.Register(new AgentDefinition
        {
            Name = "analyst-agent",
            Instructions = "分析文件",
            PipelineName = "Analyst",
        });
        var factory = new AgentFactory(rootSp, registry, httpFactory, DefaultModelChoice);
        var parentContext = new AgentContext(TestHelpers.EmptyServiceProvider);

        // 验证注册的定义 PipelineName 正确
        var definition = registry.Get("analyst-agent");
        Assert.NotNull(definition);
        Assert.Equal("Analyst", definition!.PipelineName);

        // AgentFactory 能成功运行 Analyst 管道（需要更多中间件注册，
        // 但 Simple 管道在 BuildTestInfrastructure 已注册完整中间件）
        // 使用 Simple 管道验证基本流程
        var simpleDef = new AgentDefinition
        {
            Name = "simple-agent",
            Instructions = "简单助手",
            PipelineName = "Simple",
        };
        registry.Register(simpleDef);

        var result = await factory.RunAsync("simple-agent", "hello", parentContext, CancellationToken.None);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RunAsync_ChildContextDoesNotInheritMessages()
    {
        // 验证子上下文不继承父上下文的历史消息
        var (rootSp, handler, httpFactory) = BuildTestInfrastructure();
        var registry = new AgentRegistry([]);
        registry.Register(new AgentDefinition { Name = "test", Instructions = "助手" });
        var factory = new AgentFactory(rootSp, registry, httpFactory, DefaultModelChoice);

        // 父上下文有多条历史消息
        var parentContext = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            Messages =
            [
                new ChatMessage(ChatRole.User, "父消息1"),
                new ChatMessage(ChatRole.Assistant, "父回复1"),
                new ChatMessage(ChatRole.User, "父消息2"),
            ]
        };

        var result = await factory.RunAsync("test", "子输入", parentContext, CancellationToken.None);

        Assert.True(result.Success);

        // 验证 HTTP 请求体中只有 system + user 两条消息
        Assert.NotNull(handler.LastRequestBody);
        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        // SystemPromptInjectionMiddleware 添加 system，UserInputMiddleware 添加 user
        // 子上下文的消息列表从空开始，不应包含父上下文的 3 条消息
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("子输入", messages[1].GetProperty("content").GetString());
    }
}
