using Microsoft.Extensions.AI;

namespace ManInBlack.AI;

/// <summary>
/// Agent 执行结果
/// </summary>
public sealed class AgentResult
{
    /// <summary>
    /// 最终文本回复
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// 执行步数（收到的 ChatResponseUpdate 数量）
    /// </summary>
    public int Steps { get; init; }

    /// <summary>
    /// 完整消息历史
    /// </summary>
    public IList<ChatMessage> Messages { get; init; } = [];
}
