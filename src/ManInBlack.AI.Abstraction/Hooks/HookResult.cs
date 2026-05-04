namespace ManInBlack.AI.Abstraction.Hooks;

/// <summary>
/// 钩子脚本的返回结果（从 stdout JSON 反序列化）
/// </summary>
public record HookResult
{
    /// <summary>是否阻断执行（仅 BeforeToolExecute 有效）</summary>
    public bool IsBlocked { get; init; }

    /// <summary>阻断原因，会作为 FunctionResultContent 返回给模型</summary>
    public string? BlockReason { get; init; }

    /// <summary>注入到上下文的额外文本</summary>
    public string? InjectedText { get; init; }

    /// <summary>注入目标："SystemPrompt" | "UserMessage" | "ToolResult"</summary>
    public string? InjectTarget { get; init; }

    /// <summary>脚本是否成功执行（false 表示脚本本身出错）</summary>
    public bool Succeeded { get; init; } = true;

    /// <summary>脚本错误信息（Succeeded=false 时）</summary>
    public string? ErrorMessage { get; init; }
}
