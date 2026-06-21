namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 ProviderSettings。
/// </summary>
public sealed class ProviderBuilder
{
    internal ProviderSettings Settings { get; } = new();

    public ProviderBuilder Schema(string schema) { Settings.Schema = schema; return this; }
    public ProviderBuilder ApiKey(string apiKey) { Settings.ApiKey = apiKey; return this; }
    public ProviderBuilder BaseUrl(string? baseUrl) { Settings.BaseUrl = baseUrl; return this; }
}
