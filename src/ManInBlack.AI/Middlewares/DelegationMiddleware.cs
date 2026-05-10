using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 子 Agent 委托中间件，为拥有 SubAgents 配置的 Agent 注入委托工具和提示词。
/// 只有 AgentDefinition.SubAgents 非空时才生效。
/// </summary>
[ServiceRegister.Scoped]
public class DelegationMiddleware(AgentFactory agentFactory) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var definition = agentFactory.GetDefinition(context.AgentName);

        if (definition.SubAgents.Count > 0)
        {
            // 构建子 Agent 描述列表
            var descriptions = new List<string>();
            foreach (var subAgentName in definition.SubAgents)
            {
                var subDef = agentFactory.GetDefinition(subAgentName);
                descriptions.Add($"- **{subAgentName}**: {subDef.Description}");
            }

            context.SystemPrompt += $"""

                # 可委托的子 Agent
                {string.Join(Environment.NewLine, descriptions)}

                当任务匹配某个子 Agent 的能力时，调用 `DelegateToAgent` 工具将任务委托给它。
                在 task 参数中提供足够的上下文信息，让子 Agent 能够独立完成任务。
                """;

            // 注入委托工具声明
            context.Options ??= new ChatOptions();
            context.Options.Tools ??= [];
            foreach (var tool in DelegationTools.AllToolDeclarations)
                context.Options.Tools.Add(tool);
        }

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }
}
