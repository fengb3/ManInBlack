using System.Text;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tools;

/// <summary>
/// 委托工具，允许父 Agent 将子任务委托给指定的子 Agent 执行。
/// 子 Agent 在独立的 DI 作用域中运行，拥有自己的管道和工具集。
/// </summary>
[ServiceRegister.Scoped]
public partial class DelegationTools(AgentFactory agentFactory, AgentContext parentContext, EventBus eventBus)
{
    /// <summary>
    /// 将任务委托给指定子 Agent 执行，返回子 Agent 的文本输出。
    /// 子 Agent 在独立的 DI 作用域中运行，拥有自己的管道和工具集。
    /// </summary>
    /// <param name="agentName">要委托的子 Agent 名称（必须在父 Agent 的 SubAgents 列表中）</param>
    /// <param name="task">要委托给子 Agent 的任务描述，应包含足够的上下文信息</param>
    /// <returns>子 Agent 执行完成后的文本输出</returns>
    [AiTool]
    [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    public async Task<string> DelegateToAgent(string agentName, string task)
    {
        // 验证 agentName 在父 Agent 的 SubAgents 列表中
        var definition = agentFactory.GetDefinition(parentContext.AgentName);
        if (!definition.SubAgents.Contains(agentName))
            return $"Error: 子 Agent '{agentName}' 不在可用列表中。可用的子 Agent: {string.Join(", ", definition.SubAgents)}";

        // 验证子 Agent 定义存在
        agentFactory.GetDefinition(agentName);

        var ct = parentContext.CancellationToken;

        // 预生成子 Agent 的 AgentId，通过 SubAgentStartedEvent 通知前端
        // 前端收到事件后可直接订阅该 AgentId 的事件（ModelContentEvent 等）
        var childAgentId = Guid.NewGuid().ToString();

        await eventBus.PublishAsync(parentContext.AgentId, new SubAgentStartedEvent
        {
            ParentAgentId = parentContext.AgentId,
            SubAgentName = agentName,
            SubAgentId = childAgentId,
            Task = task,
        }, ct);

        // 运行子 Agent（独立会话，parentId = 父 AgentId）
        var sb = new StringBuilder();
        try
        {
            var updates = agentFactory.RunAsync(
                agentName,
                task,
                parentContext.AgentId,
                "Agent",
                ctx => ctx.AgentId = childAgentId,
                ct);

            await foreach (var update in updates.WithCancellation(ct))
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextContent text)
                        sb.Append(text.Text);
                }
            }
        }
        catch (Exception ex)
        {
            await eventBus.PublishAsync(parentContext.AgentId, new SubAgentCompletedEvent
            {
                ParentAgentId = parentContext.AgentId,
                SubAgentName = agentName,
                SubAgentId = childAgentId,
                Error = ex.Message,
            }, ct);

            return $"Error: 子 Agent '{agentName}' 执行失败: {ex.Message}";
        }

        var result = sb.Length > 0 ? sb.ToString() : $"子 Agent '{agentName}' 未产生任何输出。";

        await eventBus.PublishAsync(parentContext.AgentId, new SubAgentCompletedEvent
        {
            ParentAgentId = parentContext.AgentId,
            SubAgentName = agentName,
            SubAgentId = childAgentId,
            Result = result,
        }, ct);

        return result;
    }
}
