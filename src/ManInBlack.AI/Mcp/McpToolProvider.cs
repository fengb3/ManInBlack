using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ManInBlack.AI.Mcp;

/// <summary>
/// MCP 工具执行入口（Singleton）。由 ToolExecutor 在静态 handler 字典 miss 时 fallback 调用。
/// 通过注入的 McpClientHostedService 拿到已连接的 client 与工具清单。
/// </summary>
public sealed class McpToolProvider(
    McpClientHostedService hosted,
    ILogger<McpToolProvider> logger) : IMcpToolProvider
{
    public bool IsMcpTool(string fullyQualifiedName)
        => hosted.ToolsByFqn.ContainsKey(fullyQualifiedName);

    public async Task<string> ExecuteAsync(
        string fullyQualifiedName,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        if (!hosted.ToolsByFqn.TryGetValue(fullyQualifiedName, out var descriptor))
            throw new McpToolException($"未找到 MCP 工具：{fullyQualifiedName}");

        if (!hosted.Clients.TryGetValue(descriptor.ServerName, out var client))
            throw new McpToolException($"MCP server 未连接：{descriptor.ServerName}");

        // 过滤 null 参数；MCP 参数值需非 null
        Dictionary<string, object?>? callArgs = null;
        if (arguments is not null)
        {
            callArgs = new Dictionary<string, object?>();
            foreach (var kv in arguments)
                if (kv.Value is not null)
                    callArgs[kv.Key] = kv.Value;
        }

        CallToolResult result;
        try
        {
            result = await client.CallToolAsync(descriptor.ToolName, callArgs, null, null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // 取消向上传播，不包装为工具失败（否则 AgentLoop 会把“取消”当“工具失败”继续循环）
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP 工具 {Fqn} 调用异常", fullyQualifiedName);
            throw new McpToolException($"MCP 工具 {fullyQualifiedName} 调用失败：{ex.Message}");
        }

        var text = ExtractText(result);
        if (result.IsError == true)
            throw new McpToolException($"MCP 工具 {fullyQualifiedName} 执行失败：{text}");

        return text;
    }

    private static string ExtractText(CallToolResult result)
    {
        var sb = new StringBuilder();
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock txt && !string.IsNullOrEmpty(txt.Text))
                sb.AppendLine(txt.Text);
            else
                // 非文本块（图像/嵌入资源等）无法转为文本，留占位提示，避免静默丢弃让模型误以为工具失败
                sb.AppendLine($"[非文本内容块：{block.Type}]");
        }
        return sb.ToString().TrimEnd();
    }
}
