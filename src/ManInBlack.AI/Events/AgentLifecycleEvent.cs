namespace ManInBlack.AI.Events;

/// <summary>
/// LLM 调用前事件，支持注入文本到 SystemPrompt
/// </summary>
public record BeforeLlmCallEvent
{
    public string AgentId { get; init; } = string.Empty;
    public string? SystemPrompt { get; init; }
    public string? UserInput { get; init; }

    /// <summary>钩子注入的文本列表，由订阅者追加</summary>
    public List<string> InjectedTexts { get; } = [];

    /// <summary>注入目标："SystemPrompt" | "UserMessage"，由订阅者设置</summary>
    public string? InjectTarget { get; set; }
}

/// <summary>
/// LLM 响应后事件（纯通知）
/// </summary>
public record AfterLlmCallEvent
{
    public string AgentId { get; init; } = string.Empty;
    public string? SystemPrompt { get; init; }
    public string? UserInput { get; init; }
}

/// <summary>
/// 工具执行前事件，支持阻断执行
/// </summary>
public record BeforeToolExecuteEvent
{
    public string AgentId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string CallId { get; init; } = string.Empty;
    public string? ArgumentsJson { get; init; }

    /// <summary>是否阻断执行，由订阅者设置</summary>
    public bool IsBlocked { get; set; }

    /// <summary>阻断原因，由订阅者设置</summary>
    public string? BlockReason { get; set; }
}

/// <summary>
/// 工具执行后事件（纯通知）
/// </summary>
public record AfterToolExecuteEvent
{
    public string AgentId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string CallId { get; init; } = string.Empty;
    public string? ArgumentsJson { get; init; }
    public string? ResultJson { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// 所有工具执行完毕事件（纯通知）
/// </summary>
public record AllToolsCompletedEvent
{
    public string AgentId { get; init; } = string.Empty;
}

/// <summary>
/// Agent 循环结束事件（纯通知）
/// </summary>
public record AgentCompletedEvent
{
    public string AgentId { get; init; } = string.Empty;
    public string? SystemPrompt { get; init; }
    public string? UserInput { get; init; }
}
