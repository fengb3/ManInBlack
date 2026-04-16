using System.Text;
using ManInBlack.AI.Attributes;
using ManInBlack.AI.Middleware;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI;

/// <summary>
/// 可执行的 Agent，封装管道调用和消息管理
/// </summary>
public class Agent
{
    public string AgentId { get; set; }
    public string ParentId { get; set; }
    
    /// <summary>
    /// can be User or 'Agent'
    /// </summary>
    public string ParentType { get; set; }
}
