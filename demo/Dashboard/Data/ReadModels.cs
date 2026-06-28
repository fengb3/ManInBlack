namespace ManInBlack.Dashboard.Data;

public sealed record SessionSummary
{
    public required string SessionId { get; init; }
    public required int MessageCount { get; init; }
    public required string FirstAt { get; init; }
    public required string LastAt { get; init; }
    public string? UserId { get; init; }
}

public sealed record UserSummary
{
    public required string UserId { get; init; }
    public required Dictionary<string, object?> Metadata { get; init; }
    public required IReadOnlyList<string> SessionIds { get; init; }
}

public enum MessageBlockKind { Text, ToolCall, ToolResult, Reasoning, Unknown }

public sealed record MessageBlock
{
    public required MessageBlockKind Kind { get; init; }
    public string? Text { get; init; }          // Text / Reasoning
    public string? ToolName { get; init; }       // ToolCall
    public string? ArgumentsJson { get; init; }  // ToolCall
    public string? ResultJson { get; init; }     // ToolResult
    public string? RawJson { get; init; }        // Unknown
}

public sealed record MessageView
{
    public required string Role { get; init; }
    public required IReadOnlyList<MessageBlock> Blocks { get; init; }
}

public sealed record SearchResult
{
    public required string SessionId { get; init; }
    public required string Snippet { get; init; }
    public required string CreatedAt { get; init; }
}
