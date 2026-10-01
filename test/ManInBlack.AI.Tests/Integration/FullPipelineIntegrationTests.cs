using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Integration;

/// <summary>
/// 全管道集成测试：用 FakeChatClient 替换真实 LLM，验证默认管道的事件顺序、
/// 工具执行、参数增量事件（ToolCallStreamEvent）与消息历史。
/// </summary>
public sealed class FullPipelineIntegrationTests : IDisposable
{
    private readonly string _rootPath;

    public FullPipelineIntegrationTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"mib-integ-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, recursive: true);
        }
        catch
        {
            // 测试清理失败不影响测试结果
        }
    }

    [Fact]
    public async Task RunAsync_StreamingToolCall_ShouldExecuteToolAndEmitExpectedEvents()
    {
        // Arrange: 两轮剧本
        // 第一轮：参数增量分片 + 完整 FunctionCallContent
        // 第二轮：最终文本回答
        var fakeClient = new FakeChatClient(new List<IReadOnlyList<ChatResponseUpdate>>
        {
            new List<ChatResponseUpdate>
            {
                new(ChatRole.Assistant,
                    [CreateDeltaFunctionCall("call-add-1", "Add", "{\"a\":")]),
                new(ChatRole.Assistant,
                    [CreateDeltaFunctionCall("call-add-1", "Add", "{\"a\":3,")]),
                new(ChatRole.Assistant,
                    [CreateDeltaFunctionCall("call-add-1", "Add", "{\"a\":3,\"b\":")]),
                new(ChatRole.Assistant,
                    [CreateDeltaFunctionCall("call-add-1", "Add", "{\"a\":3,\"b\":5}")]),
                new(ChatRole.Assistant,
                    [new FunctionCallContent("call-add-1", "Add",
                        new Dictionary<string, object?> { ["a"] = 3, ["b"] = 5 })]),
            },
            new List<ChatResponseUpdate>
            {
                new(ChatRole.Assistant, [new TextContent("3 + 5 = 8")])
            }
        });

        var fakeHookExecutor = new FakeHookExecutor();

        var services = new ServiceCollection()
            .AddManInBlack()
            .AddProvider("default", p => p.Schema("OpenAI").ApiKey("fake-key"))
            .AddModelChoice("default", c => c.Provider("default").ModelId("fake-model"))
            .UseStorage(s => s.RootPath(_rootPath))
            .AddAgent("math-agent", a => a
                .Instruction("你是一个数学助手。")
                .Pipeline("default"))
            .Services
            .AddScoped<IChatClient>(_ => fakeClient)
            .AddScoped<IHookExecutor>(_ => fakeHookExecutor)
            .BuildServiceProvider();

        var factory = services.GetRequiredService<AgentFactory>();

        var recordedEvents = new List<RecordedEvent>();
        var eventSubscriptions = new List<IDisposable>();

        // Act: 运行 Agent，在 configure 回调中订阅事件
        var updates = factory.RunAsync("math-agent", "calculate 3+5", "user-42", "User", ctx =>
        {
            var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
            var key = ctx.AgentId;
            var hookKey = EventBus.HookKey(key);

            eventSubscriptions.Add(bus.Subscribe<BeforeLlmCallEvent>(hookKey, (e, _) =>
            {
                recordedEvents.Add(new RecordedEvent(nameof(BeforeLlmCallEvent)));
                return Task.CompletedTask;
            }));
            eventSubscriptions.Add(bus.Subscribe<AfterLlmCallEvent>(hookKey, (e, _) =>
            {
                recordedEvents.Add(new RecordedEvent(nameof(AfterLlmCallEvent)));
                return Task.CompletedTask;
            }));
            eventSubscriptions.Add(bus.Subscribe<BeforeToolExecuteEvent>(key, (e, _) =>
            {
                recordedEvents.Add(new RecordedEvent(nameof(BeforeToolExecuteEvent), e.ToolName));
                return Task.CompletedTask;
            }));
            eventSubscriptions.Add(bus.Subscribe<AfterToolExecuteEvent>(key, (e, _) =>
            {
                recordedEvents.Add(new RecordedEvent(nameof(AfterToolExecuteEvent), e.ToolName));
                return Task.CompletedTask;
            }));
            eventSubscriptions.Add(bus.Subscribe<AllToolsCompletedEvent>(hookKey, (e, _) =>
            {
                recordedEvents.Add(new RecordedEvent(nameof(AllToolsCompletedEvent)));
                return Task.CompletedTask;
            }));
            eventSubscriptions.Add(bus.Subscribe<AgentCompletedEvent>(key, (e, _) =>
            {
                recordedEvents.Add(new RecordedEvent(nameof(AgentCompletedEvent)));
                return Task.CompletedTask;
            }));
            eventSubscriptions.Add(bus.Subscribe<ModelContentEvent>(key, (e, _) =>
            {
                recordedEvents.Add(new RecordedEvent(nameof(ModelContentEvent), Kind: e.Kind.ToString()));
                return Task.CompletedTask;
            }));
            eventSubscriptions.Add(bus.Subscribe<ToolCallStreamEvent>(key, (e, _) =>
            {
                recordedEvents.Add(new RecordedEvent(nameof(ToolCallStreamEvent), e.ToolName, e.ArgumentDelta));
                return Task.CompletedTask;
            }));
        });

        var outputTexts = new List<string>();
        await foreach (var update in updates)
        {
            foreach (var text in update.Contents.OfType<TextContent>())
                outputTexts.Add(text.Text);
        }

        foreach (var sub in eventSubscriptions)
            sub.Dispose();

        // Assert: 事件顺序
        var expectedSequence = new[]
        {
            nameof(BeforeLlmCallEvent),
            nameof(ToolCallStreamEvent),
            nameof(ToolCallStreamEvent),
            nameof(ToolCallStreamEvent),
            nameof(ToolCallStreamEvent),
            nameof(AfterLlmCallEvent),
            nameof(BeforeToolExecuteEvent),
            nameof(AfterToolExecuteEvent),
            nameof(AllToolsCompletedEvent),
            nameof(ModelContentEvent),  // Text（第二轮 LLM 输出）
            nameof(AfterLlmCallEvent),
            nameof(AgentCompletedEvent),
            nameof(ModelContentEvent)   // Completed（EventPublishingMiddleware 在整条流结束后发布）
        };

        var actualSequence = recordedEvents.Select(e => e.Name).ToArray();
        Assert.Equal(expectedSequence, actualSequence);

        // Assert: ToolCallStreamEvent 增量顺序
        var deltas = recordedEvents
            .Where(e => e.Name == nameof(ToolCallStreamEvent))
            .Select(e => e.Delta)
            .ToArray();
        Assert.Equal(new[] { "{\"a\":", "3,", "\"b\":", "5}" }, deltas);

        // Assert: 工具确实执行、参数正确
        var beforeTool = recordedEvents.First(e => e.Name == nameof(BeforeToolExecuteEvent));
        Assert.Equal("Add", beforeTool.ToolName);

        var afterTool = recordedEvents.First(e => e.Name == nameof(AfterToolExecuteEvent));
        Assert.Equal("Add", afterTool.ToolName);

        // Assert: 文本输出
        Assert.Contains("3 + 5 = 8", outputTexts);

        // Assert: 消息历史包含完整工具调用与结果
        // 由于 SavePersistenceMiddleware 异步落库，等待一下再查
        var sessionStorage = services.GetRequiredService<ISessionStorage>();
        var userStorage = services.GetRequiredService<IUserStorage>();
        var user = await userStorage.GetOrCreateUser("user-42");
        var sessionId = await userStorage.GetLatestSessionIdAsync("user-42", SessionSource.Interactive);
        Assert.NotNull(sessionId);

        var messages = await sessionStorage.LoadMessages(sessionId);
        var toolCallMsg = messages.LastOrDefault(m => m.Role == ChatRole.Assistant && m.Contents.OfType<FunctionCallContent>().Any());
        Assert.NotNull(toolCallMsg);
        var functionCall = toolCallMsg!.Contents.OfType<FunctionCallContent>().First();
        Assert.Equal("Add", functionCall.Name);
        Assert.Equal(3, Convert.ToInt32(functionCall.Arguments!["a"]));
        Assert.Equal(5, Convert.ToInt32(functionCall.Arguments["b"]));

        var toolResultMsg = messages.LastOrDefault(m => m.Role == ChatRole.Tool);
        Assert.NotNull(toolResultMsg);
        var functionResult = toolResultMsg!.Contents.OfType<FunctionResultContent>().First();
        Assert.Equal("call-add-1", functionResult.CallId);
        Assert.Equal("8", functionResult.Result?.ToString());
    }

    private static FunctionCallContent CreateDeltaFunctionCall(string callId, string name, string accumulatedJson)
    {
        return new FunctionCallContent(callId, name)
        {
            InformationalOnly = true,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ToolCallStreamEventKeys.IsArgumentDelta] = true,
                [ToolCallStreamEventKeys.AccumulatedArgumentsJson] = accumulatedJson
            }
        };
    }

    private sealed record RecordedEvent(string Name, string? ToolName = null, string? Delta = null, string? Kind = null);
}
