using AgentConsole;
using AgentConsole.Middlewares;
using AgentConsole.Tools;
using ManInBlack.AI;
using ManInBlack.AI.Middleware;
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
services.AddManInBlackChatClient(new ModelChoice { Provider = provider, ModelId = modelId });
services.AddTransient<CommandLineTools>();
var sp = services.BuildServiceProvider();

// 构建管道
var toolExecutor = new ToolExecutor(sp);
var pipeline = new AgentPipeline(sp);
pipeline.Use(new SystemPromptMiddleware("你是一个有用的 AI 助手。你可以通过工具执行系统命令来帮助用户完成任务。请用中文回复。"));
pipeline.Use<CommandToolMiddleware>();
pipeline.Use(new AgentLoopMiddleware(toolExecutor));
var agent = new Agent(pipeline.Build(), sp);

// 执行
Console.WriteLine("=== ManInBlack Agent Console ===");
Console.WriteLine($"模型: {provider.ProviderName} / {modelId}");
Console.WriteLine($"用户: {userInput}");
Console.WriteLine();

try
{
    var result = await agent.RunToEndAsync(userInput);
    Console.WriteLine(result.Text);
    Console.WriteLine($"\n--- 对话完成 ({result.Steps} 步) ---");
}
catch (Exception ex)
{
    Console.WriteLine($"[Error] {ex.GetType().Name}: {ex.Message}");
}
