using System.Net.Http.Headers;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI;

/// <summary>
/// 模型选择，包含协议类型、API 密钥、基础地址和模型 ID
/// </summary>
public sealed class ModelChoice
{
    /// <summary>
    /// 协议类型："OpenAI"、"Anthropic"、"Gemini"
    /// </summary>
    public string Schema { get; set; } = "";

    public string ApiKey { get; set; } = "";

    /// <summary>
    /// API 基础地址。不填时由 Schema 决定默认值。
    /// </summary>
    public string BaseUrl { get; set; } = "";

    public string ModelId { get; set; } = "";

    internal string GetEffectiveBaseUrl() => Schema switch
    {
        "OpenAI" => string.IsNullOrEmpty(BaseUrl) ? "https://api.openai.com" : BaseUrl,
        "Anthropic" => string.IsNullOrEmpty(BaseUrl) ? "https://api.anthropic.com" : BaseUrl,
        "Gemini" => string.IsNullOrEmpty(BaseUrl) ? "https://generativelanguage.googleapis.com" : BaseUrl,
        _ => BaseUrl
    };
}

public static class ChatClientProviderExtensions
{
    public static IChatClient CreateChatClient(IHttpClientFactory httpClientFactory, ModelChoice modelChoice)
    {
        var httpClient = httpClientFactory.CreateClient();
        var baseUrl = modelChoice.GetEffectiveBaseUrl();
        var baseAddress = baseUrl.EndsWith('/')
            ? new Uri(baseUrl)
            : new Uri(baseUrl + "/");

        switch (modelChoice.Schema)
        {
            case "OpenAI":
                httpClient.BaseAddress = baseAddress;
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", modelChoice.ApiKey);
                return new OpenAICompatibleChatClient(httpClient, modelChoice.ModelId);
            case "Anthropic":
                httpClient.BaseAddress = baseAddress;
                httpClient.DefaultRequestHeaders.Add("x-api-key", modelChoice.ApiKey);
                httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                return new AnthropicCompatibleChatClient(httpClient, modelChoice.ModelId);
            case "Gemini":
                httpClient.BaseAddress = baseAddress;
                return new GeminiCompatibleChatClient(httpClient, modelChoice.ApiKey, modelChoice.ModelId);
            default:
                throw new NotSupportedException($"不支持的 Schema: {modelChoice.Schema}");
        }
    }
}
