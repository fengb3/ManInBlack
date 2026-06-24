namespace ManInBlack.AI.Abstraction.Hooks;

/// <summary>
/// 传递给钩子脚本的上下文数据（通过临时文件 JSON 传入）
/// </summary>
public record HookContext
{
    /// <summary>触发此钩子的节点名称</summary>
    public string HookPoint { get; init; } = string.Empty;

    /// <summary>Agent 实例标识</summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>系统提示词（BeforeLlmCall / AfterLlmCall / AgentCompleted 时可用）</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>用户输入原文（BeforeLlmCall / AfterLlmCall 时可用）</summary>
    public string? UserInput { get; init; }

    /// <summary>工具名（BeforeToolExecute / AfterToolExecute 时可用）</summary>
    public string? ToolName { get; init; }

    /// <summary>工具调用 ID</summary>
    public string? CallId { get; init; }

    /// <summary>工具参数 JSON</summary>
    public string? ArgumentsJson { get; init; }

    /// <summary>工具执行结果 JSON（AfterToolExecute 时可用）</summary>
    public string? ResultJson { get; init; }

    /// <summary>工具执行错误信息（AfterToolExecute 时可用）</summary>
    public string? Error { get; init; }

    /// <summary>附加属性，用于传递任意键值对（如 RootUserId、SessionId 等）</summary>
    public Dictionary<string, string>? Properties { get; init; }
}
