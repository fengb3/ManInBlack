namespace ManInBlack.AI.Mcp;

/// <summary>
/// MCP 工具的执行入口。由 <c>ToolExecutor</c> 在静态 handler 字典 miss 时 fallback 调用，
/// 使 MCP 工具走和本地工具一样的 ToolExecutor 派发 + AgentLifecycleFilter 事件流。
/// </summary>
public interface IMcpToolProvider
{
    /// <summary>该完全限定名是否是已连接的 MCP 工具。</summary>
    bool IsMcpTool(string fullyQualifiedName);

    /// <summary>
    /// 调用 MCP 工具，返回聚合后的文本结果。
    /// 服务端报告失败（CallToolResult.IsError）时抛 <see cref="McpToolException"/>。
    /// </summary>
    Task<string> ExecuteAsync(string fullyQualifiedName, IDictionary<string, object?>? arguments, CancellationToken cancellationToken);
}

/// <summary>MCP 工具调用失败（服务端 IsError=true 或连接问题）。</summary>
public sealed class McpToolException(string message) : Exception(message);
