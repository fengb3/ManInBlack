using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Events;

/// <summary>
/// 模型流式输出内容事件
/// </summary>
public record ModelContentEvent
{
    /// <summary>Agent 标识</summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>内容类型</summary>
    public ModelContentKind Kind { get; init; }

    /// <summary>文本内容（Text、Reasoning 时有值）</summary>
    public string? Text { get; init; }

    /// <summary>Token 用量（Usage 时有值）</summary>
    public UsageDetails? Usage { get; init; }
}

/// <summary>
/// 模型输出内容类型
/// </summary>
public enum ModelContentKind
{
    Text,
    Reasoning,
    Usage,
    Completed
}
