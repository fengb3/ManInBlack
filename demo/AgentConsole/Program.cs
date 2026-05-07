using AgentConsole.Middlewares;
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Services;
using Microsoft.Extensions.DependencyInjection;

// 构建 DI 容器（从 ~/.man-in-black/settings.json 读取配置）
var services = new ServiceCollection();
services.AddManInBlackFromSettings()
    .AddBuiltInAgents();
services.AddScoped<ConsoleOutputMiddleware>();

var rootSp = services.BuildServiceProvider();

using var scope = rootSp.CreateScope();
var sp = scope.ServiceProvider;

var userId = "console";

var userStorage = sp.GetRequiredService<IUserStorage>();
var user = await userStorage.GetOrCreateUser(userId);

// 从注册表查找 "general" Agent 定义
var registry = sp.GetRequiredService<IAgentRegistry>();
var generalAgent = registry.Get("general")
    ?? throw new InvalidOperationException("未找到 'general' Agent，请确保调用了 AddBuiltInAgents()");

var agentContext = sp.GetRequiredService<AgentContext>();
agentContext.AgentId = Guid.NewGuid().ToString();
agentContext.ParentId = userId;
agentContext.ParentType = "Default";
agentContext.SessionId = user.GetLatestSessionId() ?? await userStorage.CreateNewSessionIdAsync(userId);
agentContext.SystemPrompt = generalAgent.Instructions;
agentContext.UserInput = args[0];

// 构建包含控制台输出的管道
var pipeline = new AgentPipelineBuilder()
    .Use<ConsoleOutputMiddleware>()
    .UseDefault()
    .Build(sp);

Console.WriteLine("=== ManInBlack Agent Console ===");
Console.WriteLine();

var updates = pipeline(agentContext);
await foreach (var _ in updates) { }
