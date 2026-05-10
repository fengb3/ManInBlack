using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Events;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Services;
using Microsoft.Extensions.DependencyInjection;


// 构建 DI 容器（从 ~/.man-in-black/settings.json 读取配置）
var services = new ServiceCollection();
services.AddManInBlackFromSettings();

// 子 Agent：翻译专家，使用 sub-agent pipeline（有文件工具和事件发布，无 DelegationMiddleware）
services.AddAgentDefinition(new AgentDefinition
{
    Name = "translator",
    Description = "翻译专家，擅长将翻译成各种语言",
    Instruction = "你是一个翻译专家。用户会给你一段文本或一个文件路径，你读取文件内容后将其翻译成自然流畅的目标语言，不需要任何额外解释。",
    PipelineName = "sub-agent"
});

// 父 Agent：协调者，可以委托给 translator 子 Agent
services.AddAgentDefinition(new AgentDefinition
{
    Name = "console-agent",
    Instruction = "你是一个AI助手。你可以通过工具执行系统命令来帮助用户完成任务。请用中文回复。",
    PipelineName = "default",
    SubAgents = ["translator"]
});


var rootSp = services.BuildServiceProvider();

// 注册子 Agent 专用 pipeline：有工具和事件发布，无 DelegationMiddleware
var factory = rootSp.GetRequiredService<AgentFactory>();
factory.RegisterPipeline("sub-agent", builder => builder
    .Use<EventPublishingMiddleware>()
    .Use<FileToolsMiddleware>()
    .UseSimple());

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

    subs.Add(bus.Subscribe<BeforeToolExecuteEvent>(key, async (@event, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[Tool Call] {@event.ToolName}({@event.ArgumentsJson})");
        Console.ResetColor();
    }));
    subs.Add(bus.Subscribe<AfterToolExecuteEvent>(key, async (@event, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Tool Result] {@event.ResultJson} {@event.Error}");
        Console.ResetColor();
    }));

    // 子 Agent 生命周期订阅
    var childSubs = new List<IDisposable>();
    subs.Add(bus.Subscribe<SubAgentStartedEvent>(key, async (evt, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[SubAgent Started] {evt.SubAgentName} (id={evt.SubAgentId})");
        Console.ResetColor();

        // 直接订阅子 Agent 的模型输出和工具事件
        childSubs.Add(bus.Subscribe<ModelContentEvent>(evt.SubAgentId, async (e, ct) =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            if (e.Kind == ModelContentKind.Text) Console.Write(e.Text);
            Console.ResetColor();
        }));
        childSubs.Add(bus.Subscribe<BeforeToolExecuteEvent>(evt.SubAgentId, async (e, ct) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"\n  [SubAgent Tool] {e.ToolName}({e.ArgumentsJson})");
            Console.ResetColor();
        }));
        childSubs.Add(bus.Subscribe<AfterToolExecuteEvent>(evt.SubAgentId, async (e, ct) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"  [SubAgent Tool Result] {e.ResultJson} {e.Error}");
            Console.ResetColor();
        }));
    }));
    subs.Add(bus.Subscribe<SubAgentCompletedEvent>(key, async (evt, ct) =>
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[SubAgent Completed] {evt.SubAgentName}");
        Console.ResetColor();
        childSubs.ForEach(s => s.Dispose());
        childSubs.Clear();
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
