namespace ManInBlack.AI.Abstraction.Factory;

/// <summary>
/// Agent 创建时的运行时参数，每次创建调用时提供
/// </summary>
public class AgentCreateOptions
{
    /// <summary>用户输入文本（必填）</summary>
    public required string UserInput { get; init; }

    /// <summary>父级标识，表示触发此 Agent 的实体</summary>
    public string ParentId { get; init; } = string.Empty;

    /// <summary>父级类型，如 "User" 或 "Agent"</summary>
    public string ParentType { get; init; } = "Default";

    /// <summary>会话标识，用于关联同一对话上下文</summary>
    public string? SessionId { get; init; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }
}