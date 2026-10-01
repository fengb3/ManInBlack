using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using Anthropic;
using Microsoft.Extensions.AI;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Microsoft;
using Mscc.GenerativeAI.Types;
using OpenAI;
using OpenAI.Chat;

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
    /// <summary>
    /// 根据 <see cref="ModelChoice"/> 创建对应的 <see cref="IChatClient"/>。
    /// 自研 SSE 客户端已退役，统一委托给官方/社区 M.E.AI 适配包：
    /// OpenAI 兼容协议 → Microsoft.Extensions.AI.OpenAI，Anthropic → Anthropic SDK，Gemini → Mscc.GenerativeAI.Microsoft。
    /// </summary>
    public static IChatClient CreateChatClient(IHttpClientFactory httpClientFactory, ModelChoice modelChoice)
    {
        return modelChoice.Schema switch
        {
            "OpenAI" => CreateOpenAICompatibleClient(httpClientFactory, modelChoice, ToBaseAddress(modelChoice.GetEffectiveBaseUrl())),
            "Anthropic" => CreateAnthropicClient(httpClientFactory, modelChoice, ToBaseAddress(modelChoice.GetEffectiveBaseUrl())),
            "Gemini" => CreateGeminiClient(httpClientFactory, modelChoice, ToBaseAddress(modelChoice.GetEffectiveBaseUrl())),
            _ => throw new NotSupportedException($"不支持的 Schema: {modelChoice.Schema}")
        };
    }

    private static Uri ToBaseAddress(string baseUrl)
    {
        return baseUrl.EndsWith('/')
            ? new Uri(baseUrl)
            : new Uri(baseUrl + "/");
    }

    private static IChatClient CreateOpenAICompatibleClient(
        IHttpClientFactory httpClientFactory,
        ModelChoice modelChoice,
        Uri baseAddress)
    {
        // 使用命名 HttpClient 作为传输层，继承 30 分钟兜底超时与 PooledConnectionLifetime 配置。
        var httpClient = httpClientFactory.CreateClient(ManInBlackHttpClients.ChatClient);

        var options = new OpenAIClientOptions
        {
            Endpoint = baseAddress,
            NetworkTimeout = TimeSpan.FromMinutes(30),
            // 禁用 SDK 内部重试：应用层 RetryMiddleware 统一负责，避免与已输出内容的流式重试叠加。
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        return new ChatClient(modelChoice.ModelId, new ApiKeyCredential(modelChoice.ApiKey), options)
            .AsIChatClient();
    }

    private static IChatClient CreateAnthropicClient(
        IHttpClientFactory httpClientFactory,
        ModelChoice modelChoice,
        Uri baseAddress)
    {
        var httpClient = httpClientFactory.CreateClient(ManInBlackHttpClients.ChatClient);
        httpClient.BaseAddress = baseAddress;

        var anthropicClient = new AnthropicClient
        {
            HttpClient = httpClient,
            ApiKey = modelChoice.ApiKey,
            BaseUrl = baseAddress.ToString().TrimEnd('/'),
            // 禁用 SDK 内部重试：应用层 RetryMiddleware 统一负责。
            MaxRetries = 0,
            // 与命名 HttpClient 保持一致，避免 10 分钟默认超时截断长流式请求。
            Timeout = TimeSpan.FromMinutes(30),
        };

        return anthropicClient.AsIChatClient(modelChoice.ModelId);
    }

    private static IChatClient CreateGeminiClient(
        IHttpClientFactory httpClientFactory,
        ModelChoice modelChoice,
        Uri baseAddress)
    {
        // Mscc.GenerativeAI 需要 IHttpClientFactory；包装命名 HttpClient，使其复用框架级超时与连接池配置。
        var factory = new NamedHttpClientFactory(httpClientFactory, ManInBlackHttpClients.ChatClient);

        var requestOptions = new Mscc.GenerativeAI.Types.RequestOptions
        {
            BaseUrl = baseAddress.ToString().TrimEnd('/'),
            Timeout = TimeSpan.FromMinutes(30),
            // 禁用内部重试：应用层 RetryMiddleware 统一负责。
            Retry = new Mscc.GenerativeAI.Types.Retry { Maximum = 0 },
        };

        var googleAi = new GoogleAI(
            apiKey: modelChoice.ApiKey,
            httpClientFactory: factory,
            requestOptions: requestOptions);

        var generativeModel = googleAi.GenerativeModel(modelChoice.ModelId);
        return new GeminiChatClient(generativeModel);
    }

    /// <summary>
    /// 把命名 HttpClient 包装成 <see cref="IHttpClientFactory"/>，供 Mscc.GenerativeAI 这类只接受工厂参数的 SDK 使用。
    /// 调用方传入的 name 会被忽略，始终返回预先配置的命名 client。
    /// </summary>
    private sealed class NamedHttpClientFactory(IHttpClientFactory inner, string name) : IHttpClientFactory
    {
        public HttpClient CreateClient(string _) => inner.CreateClient(name);
    }
}
