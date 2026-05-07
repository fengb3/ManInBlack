using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.ToolCallFilters;

namespace ManInBlack.AI.Tools;

/// <summary>
/// Sub-Agent 工具，允许主 Agent 将任务委派给已注册的子 Agent
/// </summary>
[ServiceRegister.Scoped]
public partial class SubAgentTools(IAgentFactory factory, AgentContext parentContext)
{
    /// <summary>
    /// 将任务委派给指定名称的子 Agent 执行。
    /// 子 Agent 会使用自己的系统提示词和工具集独立完成任务，并将结果返回给主 Agent。
    /// </summary>
    /// <param name="agentName">要委派任务的子 Agent 名称</param>
    /// <param name="task">需要子 Agent 执行的任务描述</param>
    /// <returns>子 Agent 的执行结果文本，失败时返回错误信息</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter>]
    public async Task<string> DelegateToAgent(string agentName, string task)
    {
        var result = await factory.RunAsync(agentName, task, parentContext, parentContext.CancellationToken);

        if (result.Success)
            return result.Output;

        return $"[Sub-agent '{agentName}' 执行失败: {result.Error?.Message}]";
    }
}
