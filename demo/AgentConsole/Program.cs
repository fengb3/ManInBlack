
using DotNetEnv;
using ManInBlack.AI;
using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;


// read comfiguration form the fucking .env file, which should be placed in the same directory as the executable, and contains the following variables:
var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envPath))
    Env.Load(envPath);
else
    Env.Load();

// 构建 DI 容器
var services = new ServiceCollection();
services.AddManInBlackCore(opt =>
{
    opt.ModelChoice = new ModelChoice
    {
        Provider = new OpenAIProvider()
        {
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
            BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "",
        },
        ModelId = Environment.GetEnvironmentVariable("OPENAI_MODEL_ID") ?? "",
    };
});


var rootSp = services.BuildServiceProvider();

using var scope = rootSp.CreateScope();
var       sp    = scope.ServiceProvider;

var agentContext = sp.GetRequiredService<AgentContext>();
agentContext.AgentId    = Guid.NewGuid().ToString();
agentContext.ParentId   = "console";
agentContext.ParentType = "User";

// middle ware 顺序, 系统提示, 持久会话

var pipeline = new AgentPipelineBuilder()
    .UseDefault()
    .Build(sp);


agentContext.SystemPrompt = "你是一个运维AI助手。你可以通过工具执行系统命令来帮助用户完成任务。请用中文回复. ";
agentContext.UserInput    = $"帮我查看当前磁盘使用情况";


var updates = pipeline(agentContext);

Console.WriteLine("=== ManInBlack Agent Console ===");
Console.WriteLine();

await foreach (ChatResponseUpdate update in updates)
{
    foreach (var content in update.Contents)
    {
        switch (content)
        {
            case TextReasoningContent reasoning:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(reasoning.Text);
                Console.ResetColor();
                break;
            case TextContent text:
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