# MCP 工具接入指南

> 本文档是 CLAUDE.md 的子文档。Agent 在修改 MCP 相关代码前应先阅读。

## 概述

ManInBlack 作为 MCP（Model Context Protocol）client，连接外部 MCP server，把 server 提供的工具暴露给模型。一次配置即可接入任意 MCP server（搜索、GitHub、数据库、文件系统等），工具运行时动态发现——无需为每种外部能力写适配代码。

`ModelContextProtocol` SDK（1.4.0）已集成，`ToolExecutor`/`ToolRegistry` 预留了运行时注册，MCP 工具走和本地 `[AiTool]` 完全一样的派发 + 事件流。

## 配置

在 `settings.json` 加 `McpServers`，键为 server 名称：

```json
"McpServers": {
  "tavily": {
    "Transport": "http",
    "Endpoint": "https://mcp.tavily.com/mcp",
    "Headers": { "Authorization": "Bearer tvly-xxx" }
  },
  "brave": {
    "Transport": "stdio",
    "Command": "npx",
    "Arguments": ["-y", "@modelcontextprotocol/server-brave-search"],
    "Environment": { "BRAVE_API_KEY": "BSAxxx" },
    "ConnectionTimeoutSeconds": 60
  },
  // 智谱 GLM-4.6V 视觉理解 MCP（stdio，npx 拉起官方 @z_ai/mcp-server）。
  // 复用智谱 key；工具暴露为 glm-vision__image_analysis / ui_to_artifact 等。
  "glm-vision": {
    "Transport": "stdio",
    "Command": "npx",
    "Arguments": ["-y", "@z_ai/mcp-server@latest"],
    "Environment": {
      "Z_AI_API_KEY": "<智谱 API Key>",
      "Z_AI_MODE": "ZHIPU"
    },
    "ConnectionTimeoutSeconds": 60
  }
}
```

| 字段 | 适用 | 说明 |
| --- | --- | --- |
| `Transport` | 两者 | `"stdio"`（子进程）或 `"http"`（SSE/Streamable HTTP）。留空按 Endpoint/Command 自动推断 |
| `Command`/`Arguments`/`WorkingDirectory`/`Environment` | stdio | 子进程配置 |
| `Endpoint`/`Headers`/`TransportMode` | http | 端点、请求头（常放 Authorization/API key）、传输模式（默认 AutoDetect） |
| `ConnectionTimeoutSeconds` | 两者 | 连接/初始化超时；stdio 首次启动建议 60+ |
| `Enabled` | 两者 | false 跳过此 server |

## 工作机制

1. **连接**：应用启动时 `McpClientHostedService`（`IHostedService`，Singleton）按配置连接各 server；单个失败只记日志，不阻断应用启动。
2. **列举与注册**：连接后 `ListToolsAsync` 列举工具，以 `"{serverName}__{toolName}"` 命名（避免与本地工具/跨 server 撞名），`Register` 到 `ToolRegistry`（Group=`"mcp"`），模型即可见。
3. **调用**：模型调用 → `FunctionCallContent` → `AgentLoopMiddleware` → `ToolExecutor`。`ToolExecutor` 静态 handler 字典 miss 时 fallback 到 `IMcpToolProvider`，**内联包 `AgentLifecycleFilter`**（从请求 scope 取，复用本地工具的事件链：飞书卡片、audit hook、`IsBlocked` 阻断）后调 `CallToolAsync`，结果聚合文本回填。
4. **生命周期**：应用停止时逐个 `DisposeAsync`（stdio 发 shutdown 给子进程）。

> 关键：`AgentLoopMiddleware` 对 MCP 完全无感知——所有 `FunctionCallContent` 统一进 `ToolExecutor`，MCP 工具声明已在 `ToolRegistry` 被 `ToolsMiddleware` 自动注入。

## 工具命名

MCP 工具在框架内全局名为 `"{serverName}__{toolName}"`（如 `tavily__tavily-search`）。这是模型看到的工具名、`FunctionCallContent.Name`，也是飞书卡片显示的标识。如需中文显示名，在飞书 `ToolExecutionCardView.ToolDisplayNameMap` 加映射。

## 限制与注意

- **stdio 子进程**：Windows 需 `npx`/`node` 在 PATH；Linux 首次 `npx -y` 下载慢，建议镜像预装或加大 `ConnectionTimeoutSeconds`。stderr 已接 `StandardErrorLines` 防管道阻塞。
- **Windows 命令包裹**：`Command` 写 `npx`/`node` 等即可，**无需**手动加 `cmd /c`——MCP SDK 的 `StdioClientTransport` 在 Windows 上会自动用 `cmd.exe /c` 包裹非 shell 命令（`StdioClientTransport.cs`，注释原文 "usually npx or uvicorn"）。故 `Command: "npx"` 在 Windows/Linux 通用。
- **stdio 文件路径**：stdio MCP 子进程在宿主文件系统运行，**不受** Agent 工作空间隔离约束。如 GLM 视觉 MCP 的 `image_source` 传宿主绝对路径（如 `C:/Users/.../x.png`）即可，正斜杠在 Windows 下 Node 亦兼容。
- **http 境外**：官方搜索 MCP server（Brave/Tavily）在境外，国内部署（如阿里云）访问可能需代理。
- **重试交互**：MCP 工具异常被 `ToolExecutor` 写入 `ctx.Error`；`RetryMiddleware` 若作用于工具层可能对付费 API 重复计费，注意配置。
- **filter 复用**：MCP 工具不走源生成器，首版仅内联挂 `AgentLifecycleFilter`；本地 `[AiTool.HasFilter<T>]` 的其他 filter（如 `LoggingFilter`）不自动挂。

## 关键文件

- `src/ManInBlack.AI/Mcp/McpClientHostedService.cs`（连接 + 列举 + 注册声明）
- `src/ManInBlack.AI/Mcp/McpToolProvider.cs`（`IsMcpTool`/`ExecuteAsync`）
- `src/ManInBlack.AI/Mcp/McpToolDeclaration.cs`、`McpToolDescriptor.cs`、`IMcpToolProvider.cs`
- `src/ManInBlack.AI/Tools/ToolExecutor.cs`（MCP fallback + 内联 filter）
- `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs`（`McpServers`/`McpServerSettings`）

## 扩展

新增 MCP server 只需在 `McpServers` 加配置，无需改框架代码。新增传输类型或能力（资源/提示）需扩展 `McpClientHostedService`/`McpToolProvider`。
