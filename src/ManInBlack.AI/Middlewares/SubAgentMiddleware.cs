using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// Sub-Agent 中间件，给主 Agent 添加子 Agent 委派能力
/// 当有已注册的子 Agent 时，在系统提示词中注入可用子 Agent 列表
/// 工具声明由源码生成的 SubAgentToolsMiddleware 负责注入
/// </summary>
[ServiceRegister.Scoped]
public class SubAgentMiddleware(IAgentRegistry registry) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 有已注册的子 Agent 时才注入提示词
        var agents = registry.GetAll();
        if (agents.Count > 0)
        {
            var agentList = string.Join("\n", agents.Select(a => $"- **{a.Name}**: {a.Description}"));

            context.SystemPrompt += $"""

                # 可用的 Sub-Agent
                你可以通过 `DelegateToAgent` 工具将任务委派给专业的子 Agent。
                可用列表：
                {agentList}
                """;
        }

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }
}
