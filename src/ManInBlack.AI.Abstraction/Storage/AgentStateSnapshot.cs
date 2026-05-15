namespace ManInBlack.AI.Abstraction.Storage;

/// <summary>
/// Agent 状态快照，用于崩溃恢复和断点续传
/// </summary>
public sealed record AgentStateSnapshot
{
    public string SessionId { get; init; } = "";
    public string AgentName { get; init; } = "";
    public string SystemPrompt { get; init; } = "";
    public Dictionary<string, object> Items { get; init; } = [];
    public DateTimeOffset SavedAt { get; init; }
    /// <summary>
    /// 检查点原因："ToolCallCompleted" 或 "SessionEnd"
    /// </summary>
    public string? CheckpointReason { get; init; }
}
