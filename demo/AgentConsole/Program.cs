using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Services;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.AI;
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
IDisposable? toolExecutingSub = null;
IDisposable? toolExecutedSub = null;

var updates = factory.RunAsync("console-agent", args[0], "console", "Default", ctx =>
{
    capturedContext = ctx;

    // 在 Factory 的 scope 内订阅 EventBus，确保能收到事件
    var bus = ctx.ServiceProvider.GetRequiredService<EventBus>();
    toolExecutingSub = bus.Subscribe<ToolExecutingEvent>(async (@event, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[Tool Call] {@event.ToolName}({string.Join(", ", @event.Arguments.Select(kv => $"{kv.Key}: {kv.Value}"))})");
        Console.ResetColor();
    });
    toolExecutedSub = bus.Subscribe<ToolExecutedEvent>(async (@event, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Tool Result] {@event.Result} {@event.Exception}");
        Console.ResetColor();
    });
});

var last = "";

await foreach (ChatResponseUpdate update in updates)
{
    foreach (var content in update.Contents)
    {
        switch (content)
        {
            case TextReasoningContent reasoning:

                if (last != "reasoning")
                {
                    Console.WriteLine("[Reasoning]");
                }

                last = "reasoning";
                
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(reasoning.Text);
                Console.ResetColor();
                break;
            case TextContent text:

                if (last != "text")
                {
                    Console.WriteLine();
                }
                
                last = "text";
                Console.Write(text.Text);
                break;
            case UsageContent:
                // usage 由 AgentLoopMiddleware 累积，不显示
                break;
        }
    }
}

// 清理 EventBus 订阅
toolExecutingSub?.Dispose();
toolExecutedSub?.Dispose();

Console.WriteLine();
Console.WriteLine();
var usage = capturedContext?.AccumulatedUsage;
if (usage is not null && (usage.InputTokenCount is not null || usage.OutputTokenCount is not null))
{
    Console.WriteLine($"Token 用量 — 输入: {usage.InputTokenCount}, 输出: {usage.OutputTokenCount}, 总计: {usage.TotalTokenCount}, 缓存: {usage.CachedInputTokenCount}");
}