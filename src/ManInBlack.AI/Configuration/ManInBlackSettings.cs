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
