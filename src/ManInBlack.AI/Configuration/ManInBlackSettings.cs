namespace ManInBlack.AI.Configuration;

public class ManInBlackSettings
{
    public string Provider { get; set; } = "OpenAI";
    public string ApiKey { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string ModelId { get; set; } = "";
    public FeishuSettings? Feishu { get; set; }

    /// <summary>
    /// 全局钩子配置列表，对所有用户生效。脚本路径相对于 {RootPath}/hooks/ 目录。
    /// </summary>
    public List<HookSettings> Hooks { get; set; } = [];

    /// <summary>
    /// 模型配置字典。键为模型别名（如 "gpt4"、"claude"），值为对应的连接参数。
    /// </summary>
    public Dictionary<string, ModelChoiceSettings>? Models { get; set; }

    /// <summary>
    /// Agent 配置字典。键为 Agent 名称，值为该 Agent 的行为参数。
    /// </summary>
    public Dictionary<string, AgentSettings>? Agents { get; set; }
}

/// <summary>
/// 模型连接配置，可被多个 Agent 复用。
/// </summary>
public class ModelChoiceSettings
{
    /// <summary>
    /// 提供商名称，如 "OpenAI"、"Anthropic"。默认 "OpenAI"。
    /// </summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>
    /// API 密钥。
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// 自定义 API 基地址（用于代理或兼容接口）。
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 模型标识符，如 "gpt-4o"、"claude-3-sonnet"。
    /// </summary>
    public string ModelId { get; set; } = "";
}

/// <summary>
/// Agent 配置，定义一个 Agent 的行为和能力。
/// </summary>
public class AgentSettings
{
    /// <summary>
    /// Agent 的功能描述，供 LLM 在选择 Agent 时参考。
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Agent 的系统提示词 / 指令。
    /// </summary>
    public string Instructions { get; set; } = "";

    /// <summary>
    /// 管道名称，决定 Agent 使用哪组中间件。内置值："Default", "Simple", "Coder", "Shell", "Analyst"。
    /// </summary>
    public string? Pipeline { get; set; }

    /// <summary>
    /// 引用 <see cref="ManInBlackSettings.Models"/> 字典中的键，指定该 Agent 使用的模型。
    /// 为 null 时使用顶层默认模型。
    /// </summary>
    public string? Model { get; set; }
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
