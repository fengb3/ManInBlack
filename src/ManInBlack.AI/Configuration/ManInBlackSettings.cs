using ManInBlack.AI.Abstraction.Storage;

namespace ManInBlack.AI.Configuration;

public class ManInBlackSettings
{
    public Dictionary<string, ProviderSettings> Providers { get; set; } = new();
    public Dictionary<string, ModelChoiceSettings> ModelChoices { get; set; } = new();
    public FeishuSettings? Feishu { get; set; }

    /// <summary>
    /// 全局钩子配置列表，对所有用户生效。脚本路径相对于 {RootPath}/hooks/ 目录。
    /// </summary>
    public List<HookSettings> Hooks { get; set; } = [];

    /// <summary>
    /// Agent 定义配置，键为 Agent 名称。从 settings.json 的 "Agents" 节加载，自动注册到 DI。
    /// </summary>
    public Dictionary<string, AgentSettings> Agents { get; set; } = new();

    /// <summary>
    /// 存储与工作空间配置
    /// </summary>
    public StorageSettings Storage { get; set; } = new();

    /// <summary>
    /// 是否启用 Linux 下的 bubblewrap 沙盒执行命令。默认 false。
    /// </summary>
    public bool UseSandbox { get; set; }

    /// <summary>
    /// MCP server 配置，键为 server 名称。应用启动时按此连接 MCP client，
    /// 其提供的工具以 "{serverName}__{toolName}" 命名注入。详见 docs/mcp-guide.md。
    /// </summary>
    public Dictionary<string, McpServerSettings> McpServers { get; set; } = new();
}

public class StorageSettings
{
    public string? RootPath { get; set; }

    public WorkspaceSettings? Workspace { get; set; }

    /// <summary>文件隔离配置(额外只读根)。经 StorageBuilder.AddReadableRoot 写入。</summary>
    public FileIsolationSettings? FileIsolation { get; set; }
}

/// <summary>
/// 文件隔离配置:经配置显式追加的只读根。同时供 bwarp 挂载与 FileTools 校验。
/// </summary>
public class FileIsolationSettings
{
    /// <summary>额外只读根(系统运行时路径、MIB 指定路径等)。</summary>
    public List<string> ReadableRoots { get; set; } = [];

    /// <summary>
    /// 注入沙盒的环境变量(env 名 → 明文值)。值在沙盒内以环境变量形式可见,
    /// 供 agent 经 RunBash 调用的 CLI 工具读取(如 FEISHU_APP_ID / OPENAI_API_KEY)。
    /// 仅作用于 bwarp 执行路径(UseSandbox=true);settings.json 文件本身仍不可见。
    /// 注意:沙盒内命令可读出这些值(如 echo),只注入你信任 agent 可见的密钥。
    /// </summary>
    public Dictionary<string, string> InjectedEnv { get; set; } = new();
}

/// <summary>
/// 单个 Provider 的 API 访问配置
/// </summary>
public class ProviderSettings
{
    /// <summary>
    /// 协议类型，只允许 "OpenAI"、"Anthropic"、"Gemini"
    /// </summary>
    public string Schema { get; set; } = "OpenAI";

    public string ApiKey { get; set; } = "";

    /// <summary>
    /// API 基础地址，可选。不填则由 Schema 决定默认值。
    /// </summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// 模型选择配置，关联一个 Provider 和一个 ModelId
/// </summary>
public class ModelChoiceSettings
{
    /// <summary>
    /// 引用的 Provider 名称（对应 Providers 字典中的 key）
    /// </summary>
    public string ProviderName { get; set; } = "";

    public string ModelId { get; set; } = "";
}

public class FeishuSettings
{
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string VerificationToken { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "https://open.feishu.cn/";

    /// <summary>
    /// 是否启用飞书 WebSocket 长连接接收事件。与 <see cref="WebhookEndpoint"/> 可同时启用。
    /// </summary>
    public bool EnableWebSocket { get; set; }

    /// <summary>
    /// 飞书 Webhook 接收事件的 API 端点路径（如 "/feishu/event/v2"）。
    /// 与 <see cref="EnableWebSocket"/> 可同时启用。
    /// </summary>
    public string? WebhookEndpoint { get; set; }
}

/// <summary>
/// 单个 Agent 的配置，对应 settings.json 中 "Agents" 字典的一个条目。键即为 Agent 名称。
/// </summary>
public class AgentSettings
{
    /// <summary>
    /// Agent 描述，用于子 Agent 委托时的提示词生成
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string Instruction { get; set; } = string.Empty;

    /// <summary>
    /// 管道名称，决定使用哪套中间件组合。默认 "default"。
    /// </summary>
    public string PipelineName { get; set; } = "default";

    /// <summary>
    /// 可委托的子 Agent 名称列表。如果非空，DelegationMiddleware 会注入委托工具和提示词。
    /// </summary>
    public List<string> SubAgents { get; set; } = [];

    /// <summary>
    /// 引用的 ModelChoice 名称（可选）。不填则使用全局默认 ModelChoice。
    /// </summary>
    public string? ModelChoiceName { get; set; }
}

/// <summary>
/// 单个 MCP server 的连接配置。Transport 决定走 stdio（子进程）还是 http（SSE/Streamable HTTP）。
/// </summary>
public class McpServerSettings
{
    /// <summary>"stdio" | "http"。留空时按 Endpoint/Command 自动推断。</summary>
    public string Transport { get; set; } = "";

    /// <summary>stdio: 可执行命令（如 "npx"、"node"、"dotnet"）。</summary>
    public string? Command { get; set; }

    /// <summary>stdio: 命令参数。</summary>
    public List<string>? Arguments { get; set; }

    /// <summary>stdio: 子进程工作目录。</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>stdio: 子进程环境变量。</summary>
    public Dictionary<string, string?>? Environment { get; set; }

    /// <summary>http: MCP server 端点（如 "https://mcp.tavily.com/mcp"）。</summary>
    public string? Endpoint { get; set; }

    /// <summary>http: 额外请求头（常用于放 Authorization / API key）。</summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>http: 传输模式 "AutoDetect"|"Sse"|"StreamableHttp"。默认 AutoDetect。</summary>
    public string? TransportMode { get; set; } = "AutoDetect";

    /// <summary>连接/初始化超时（秒）。默认 30。stdio 首次启动建议 60+。</summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>stdio: 子进程关闭超时（秒）。默认 5。独立于连接超时，避免重启时等待过久。</summary>
    public int ShutdownTimeoutSeconds { get; set; } = 5;

    /// <summary>是否启用。false 则跳过此 server。默认 true。</summary>
    public bool Enabled { get; set; } = true;
}
