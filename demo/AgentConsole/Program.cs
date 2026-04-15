using AgentConsole;
using AgentConsole.Middlewares;
using AgentConsole.Tools;
using ManInBlack.AI;
using ManInBlack.AI.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// ============ 配置（替换为你自己的值） ============
var provider = new ZhipuCodingPlanProvider() { ApiKey = "YOUR_ZHIPU_API_KEY_HERE" };
const string modelId = "glm-4.7";
// =================================================

// 从命令行参数获取用户输入
var userInput = string.Join(" ", args);
if (string.IsNullOrWhiteSpace(userInput))
{
    Console.WriteLine("用法: AgentConsole <你的问题>");
    return;
}

// 构建 DI 容器
var services = new ServiceCollection();
services.AddManInBlack(opt =>
{
    opt.ModelChoice = new ModelChoice
    {
        Provider = provider,
        ModelId = modelId
    };
});
services.AddAutoRegisteredServices();
services.AddToolExecutor();

var rootSp = services.BuildServiceProvider();


using var scope = rootSp.CreateScope();
var sp = scope.ServiceProvider;

var pipeline = new AgentPipelineBuilder()
        .Use(new SystemPromptMiddleware("你是一个有用的 AI 助手。你可以通过工具执行系统命令来帮助用户完成任务。请用中文回复。"))
        .Use<CommandToolMiddleware>()
        .Use<AgentLoopMiddleware>()
        .Build(sp)
    ;

var agentContext = sp.GetRequiredService<AgentContext>();
agentContext.Messages.Add(new ChatMessage(ChatRole.User, userInput));

var updates = pipeline(agentContext);

Console.WriteLine("=== ManInBlack Agent Console ===");
Console.WriteLine($"模型: {provider.ProviderName} / {modelId}");
Console.WriteLine($"用户: {userInput}");
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
        }
    }
}