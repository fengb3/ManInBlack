namespace ManInBlack.AI.Configuration;

public class ManInBlackSettings
{
    public string Provider { get; set; } = "OpenAI";
    public string ApiKey { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string ModelId { get; set; } = "";
    public FeishuSettings? Feishu { get; set; }
}

public class FeishuSettings
{
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string VerificationToken { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "https://open.feishu.cn/";
}
