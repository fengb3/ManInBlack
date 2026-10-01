namespace ManInBlack.AI.Events;

/// <summary>
/// 流式工具调用参数增量标记键。由产生增量的客户端/中间件写入
/// <see cref="Microsoft.Extensions.AI.AIContent.AdditionalProperties"/>，
/// <see cref="Middlewares.EventPublishingMiddleware"/> 识别后发布 <see cref="ToolCallStreamEvent"/>。
/// </summary>
public static class ToolCallStreamEventKeys
{
    /// <summary>标记此 <see cref="Microsoft.Extensions.AI.FunctionCallContent"/> 为参数增量，不应被 AgentLoop 执行</summary>
    public const string IsArgumentDelta = "__mib_arg_delta";

    /// <summary>累积到本次为止的完整参数 JSON 字符串。用于计算 <see cref="ToolCallStreamEvent.ArgumentDelta"/></summary>
    public const string AccumulatedArgumentsJson = "__mib_arg_delta_json";
}

/// <summary>
/// 流式工具调用参数增量事件。
/// 当底层 <see cref="Microsoft.Extensions.AI.IChatClient"/> 在流式响应中按 token 返回工具调用参数时，
/// <see cref="Middlewares.EventPublishingMiddleware"/> 把每个分片广播为一次本事件，
/// 供 Dashboard / FeishuCardSession 等 UI 观察者实时渲染参数拼装过程。
/// </summary>
public record ToolCallStreamEvent
{
    /// <summary>Agent 标识</summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>工具调用 ID。流式期间可能为空，finish 分片才补齐</summary>
    public string CallId { get; init; } = string.Empty;

    /// <summary>工具名称。首个分片通常即可确定</summary>
    public string? ToolName { get; init; }

    /// <summary>本次参数增量（原始 JSON 字符串片段）</summary>
    public string ArgumentDelta { get; init; } = string.Empty;

    /// <summary>截至目前累积的完整参数 JSON 字符串</summary>
    public string AccumulatedArguments { get; init; } = string.Empty;
}
