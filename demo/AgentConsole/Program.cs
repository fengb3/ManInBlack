using AgentConsole;
using AgentConsole.Middlewares;
using AgentConsole.Tools;
using ManInBlack.AI;
using ManInBlack.AI.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// ============ 配置（替换为你自己的值） ============
var provider = new ZhipuCodingPlanProvider()
{
    ApiKey = "YOUR_ZHIPU_API_KEY_HERE",
};
const string modelId = "glm-4.7";
// =================================================



// 构建 DI 容器
var services = new ServiceCollection();
services.AddManInBlack(opt => {
    opt.ModelChoice = new ModelChoice { Provider = provider, ModelId = modelId };
});
services.AddAutoRegisteredServices();
services.AddToolExecutor();

var rootSp = services.BuildServiceProvider();

using var scope = rootSp.CreateScope();
var       sp    = scope.ServiceProvider;

var agent = sp.GetRequiredService<Agent>();
agent.AgentId    = Guid.NewGuid().ToString();
agent.ParentId   = "console";
agent.ParentType = "User";

// middle ware 顺序, 系统提示, 持久会话

var pipeline = new AgentPipelineBuilder()
    .Use<SystemPromptMiddleware>()
    .Use<ReadPersistenceMiddleware>()
    .Use<SavePersistenceMiddleware>()
    .Use<UserInputMiddleware>()
    // .Use<CommandToolMiddleware>()
    .Use<SimpleMathToolMiddleware>()
    .Use<AgentLoopMiddleware>()// Agent Loop 应该在最后一个
    .Build(sp);

// 构造随机数学表达式
var rng = new Random();
List<int> nums = Enumerable.Range(0, 4).Select(_ => rng.Next(1, 100)).ToList();
List<string> ops = ["+", "-", "*", "/"];
List<string> selectedOps = Enumerable.Range(0, 3).Select(_ => ops[rng.Next(ops.Count)]).ToList();

int bracketPos = rng.Next(0, 3);

var expression = "";
for (int i = 0; i < nums.Count; i++)
{
    if (i == bracketPos) expression += "(";
    expression += nums[i];
    if (i == bracketPos + 1) expression += ")";
    if (i < selectedOps.Count) expression += " " + selectedOps[i] + " ";
}


var agentContext = sp.GetRequiredService<AgentContext>();
agentContext.UserInput    = $"请计算以下数学表达式的结果: {expression}。请详细说明你的计算过程。请使用计算工具来计算，并在最后给出结果。";
agentContext.SystemPrompt = "你是一个有用的 AI 助手。你可以通过工具执行系统命令来帮助用户完成任务。请用中文回复. ";

var updates = pipeline(agentContext);

Console.WriteLine("=== ManInBlack Agent Console ===");
Console.WriteLine($"模型: {provider.ProviderName} / {modelId}");
Console.WriteLine($"用户: {agentContext.UserInput}");
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