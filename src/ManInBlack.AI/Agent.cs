namespace ManInBlack.AI;

/// <summary>
/// 可执行的 Agent，封装管道调用和消息管理
/// </summary>
public class Agent
{
    public string AgentId { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;

    /// <summary>
    /// can be User or 'Agent'
    /// </summary>
    public string ParentType { get; set; } = string.Empty;
}
