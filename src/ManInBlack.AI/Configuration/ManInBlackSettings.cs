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
