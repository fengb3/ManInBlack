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
}

public class StorageSettings
{
    public string? RootPath { get; set; }

    public WorkspaceSettings? Workspace { get; set; }
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
