namespace FeishuAdaptor;

/// <summary>
/// 飞书适配器配置，对应 settings.json 的 "Feishu" 节。
/// </summary>
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
