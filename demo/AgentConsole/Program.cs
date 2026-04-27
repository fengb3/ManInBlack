using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Services;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;


// 构建 DI 容器（从 ~/.man-in-black/settings.json 读取配置）
var services = new ServiceCollection();
services.AddManInBlackFromSettings();


var rootSp = services.BuildServiceProvider();

using var scope = rootSp.CreateScope();
var       sp    = scope.ServiceProvider;

var userId = "console";

var userStorage = scope.ServiceProvider.GetRequiredService<IUserStorage>();
var user = await userStorage.GetOrCreateUser(userId);


var agentContext = sp.GetRequiredService<AgentContext>();
agentContext.AgentId    = Guid.NewGuid().ToString();
agentContext.ParentId   = userId;
agentContext.ParentType = "Default";
agentContext.SessionId = user.GetLatestSessionId() ?? await userStorage.CreateNewSessionIdAsync(userId);


// middle ware 顺序, 系统提示, 持久会话

var pipeline = new AgentPipelineBuilder()
    .UseDefault()
    .Build(sp);


agentContext.SystemPrompt = "你是一个AI助手。你可以通过工具执行系统命令来帮助用户完成任务。请用中文回复. ";
agentContext.UserInput    = $"""
                             现在你正在测试运行环境
                             请执行以下命令并告诉我。是否可以执行成功
                             ls /home        # 应为空（tmpfs 隐藏）
                             ls /root        # 应为空（tmpfs 隐藏）
                             cat /etc/os-release  # 可读（系统文件）
                             touch /test.txt # 应失败（只读）
                             dotnet build    # 正常工作
                             """;



var updates = pipeline(agentContext);

Console.WriteLine("=== ManInBlack Agent Console ===");
Console.WriteLine();

// register tool call

var eventBus = sp.GetRequiredService<EventBus>();

eventBus.Subscribe<ToolExecutingEvent>(async (@event, ct) =>
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"\n[Tool Call] {@event.ToolName}({string.Join(", ", @event.Arguments.Select(kv => $"{kv.Key}: {kv.Value}"))})");
    Console.ResetColor();
});

eventBus.Subscribe<ToolExecutedEvent>(async (@event, ct) =>
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[Tool Result] {@event.Result} {@event.Exception}");
    Console.ResetColor();
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

Console.WriteLine();
Console.WriteLine();
var usage = agentContext.AccumulatedUsage;
if (usage.InputTokenCount is not null || usage.OutputTokenCount is not null)
{
    Console.WriteLine($"Token 用量 — 输入: {usage.InputTokenCount}, 输出: {usage.OutputTokenCount}, 总计: {usage.TotalTokenCount}");
}