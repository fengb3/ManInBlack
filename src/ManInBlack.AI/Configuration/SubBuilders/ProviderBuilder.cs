namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 ProviderSettings。
/// </summary>
public sealed class ProviderBuilder
{
    internal ProviderSettings Settings { get; } = new();

    /// <summary>设置 Provider 协议类型（OpenAI/Anthropic/Gemini）。</summary>
    public ProviderBuilder Schema(string schema) { Settings.Schema = schema; return this; }

    /// <summary>设置 API 密钥。</summary>
    public ProviderBuilder ApiKey(string apiKey) { Settings.ApiKey = apiKey; return this; }

    /// <summary>设置自定义 API 端点（可选）。</summary>
    public ProviderBuilder BaseUrl(string? baseUrl) { Settings.BaseUrl = baseUrl; return this; }
}
