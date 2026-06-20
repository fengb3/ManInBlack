using ModelContextProtocol.Client;

namespace ManInBlack.AI.Mcp;

/// <summary>
/// 单个 MCP 工具的描述：来自哪个 server、原始工具名、框架内全局唯一的完全限定名，以及 SDK 工具对象。
/// </summary>
public sealed record McpToolDescriptor(
    string ServerName,
    string ToolName,           // server 内的原始工具名
    string FullyQualifiedName, // "{ServerName}__{ToolName}"，避免与本地工具/跨 server 撞名
    McpClientTool Tool);       // SDK 工具对象（AIFunction，自带 Name/Description/JsonSchema）
