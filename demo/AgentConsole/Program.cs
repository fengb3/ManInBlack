using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.DependencyInjection;


// 构建 DI 容器（从 ~/.man-in-black/settings.json 读取配置）
var services = new ServiceCollection();
services.AddManInBlackFromSettings();
services.AddAgentDefinition(new AgentDefinition
{
    Name = "console-agent",
    Instruction = "你是一个AI助手。你可以通过工具执行系统命令来帮助用户完成任务。请用中文回复。",
    PipelineName = "default"
});


var rootSp = services.BuildServiceProvider();

Console.WriteLine("=== ManInBlack Agent Console ===");
Console.WriteLine();

// 通过 AgentFactory 运行 agent
var factory = rootSp.GetRequiredService<AgentFactory>();
AgentContext? capturedContext = null;
var subs = new List<IDisposable>();

var updates = factory.RunAsync("console-agent", args[0], "console", "Default", ctx =>
{
    capturedContext = ctx;

    var key = ctx.AgentId;
    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();

    // 订阅模型输出内容事件
    var last = "";
    subs.Add(bus.Subscribe<ModelContentEvent>(key, async (evt, ct) =>
    {
        switch (evt.Kind)
        {
            case ModelContentKind.Reasoning:
                if (last != "reasoning")
                    Console.WriteLine("[Reasoning]");
                last = "reasoning";
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(evt.Text);
                Console.ResetColor();
                break;
            case ModelContentKind.Text:
                if (last != "text")
                    Console.WriteLine();
                last = "text";
                Console.Write(evt.Text);
                break;
        }
    }));

    subs.Add(bus.Subscribe<ToolExecutingEvent>(key, async (@event, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[Tool Call] {@event.ToolName}({string.Join(", ", @event.Arguments.Select(kv => $"{kv.Key}: {kv.Value}"))})");
        Console.ResetColor();
    }));
    subs.Add(bus.Subscribe<ToolExecutedEvent>(key, async (@event, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Tool Result] {@event.Result} {@event.Exception}");
        Console.ResetColor();
    }));
});

// 仅驱动枚举，输出由 EventBus handler 处理
await foreach (var _ in updates) { }

// 清理 EventBus 订阅
foreach (var sub in subs) sub.Dispose();

Console.WriteLine();
Console.WriteLine();
var usage = capturedContext?.AccumulatedUsage;
if (usage is not null && (usage.InputTokenCount is not null || usage.OutputTokenCount is not null))
{
    Console.WriteLine($"Token 用量 — 输入: {usage.InputTokenCount}, 输出: {usage.OutputTokenCount}, 总计: {usage.TotalTokenCount}, 缓存: {usage.CachedInputTokenCount}");
}