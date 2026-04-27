using System.Net.Http.Headers;
using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI;

/// <summary>
/// OpenAI 提供商
/// </summary>
public sealed class OpenAIProvider : ModelProvider
{
    public override string ProviderName => "OpenAI";
    public override string BaseUrl { get; set; } = "https://api.openai.com";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// Anthropic 提供商
/// </summary>
public sealed class AnthropicProvider : ModelProvider
{
    public override string ProviderName => "Anthropic";
    public override string BaseUrl { get; set; } = "https://api.anthropic.com";
    public override string CompatibleWith => "Anthropic";
}

/// <summary>
/// Gemini 提供商
/// </summary>
public sealed class GeminiProvider : ModelProvider
{
    public override string ProviderName => "Gemini";
    public override string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public override string CompatibleWith => "Gemini";
}

/// <summary>
/// Kimi CN 提供商
/// </summary>
public sealed class KimiCNProvider : ModelProvider
{
    public override string ProviderName => "Kimi-cn";
    public override string BaseUrl { get; set; } = "https://api.moonshot.cn";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// Kimi AI 提供商
/// </summary>
public sealed class KimiAIProvider : ModelProvider
{
    public override string ProviderName => "Kimi-ai";
    public override string BaseUrl { get; set; } = "https://api.moonshot.ai";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// DeepSeek 提供商
/// </summary>
public sealed class DeepSeekProvider : ModelProvider
{
    public override string ProviderName => "DeepSeek";
    public override string BaseUrl { get; set; } = "https://api.deepseek.com";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// 通义千问 提供商（阿里巴巴）
/// </summary>
public sealed class QwenProvider : ModelProvider
{
    public override string ProviderName => "Qwen";
    public override string BaseUrl { get; set; } = "https://dashscope.aliyuncs.com/compatible-mode";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// 智谱 提供商
/// </summary>
public sealed class ZhipuProvider : ModelProvider
{
    public override string ProviderName => "Zhipu";
    public override string BaseUrl { get; set; } = "https://open.bigmodel.cn/api/paas/v4";
    public override string CompatibleWith => "OpenAI";
}

public sealed class ZhipuCodingPlanProvider : ModelProvider
{
    public override string ProviderName => "ZhipuCodingPlan";
    public override string BaseUrl { get; set; } = "https://open.bigmodel.cn/api/coding/paas/v4";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// 零一万物 提供商
/// </summary>
public sealed class YiProvider : ModelProvider
{
    public override string ProviderName => "Yi";
    public override string BaseUrl { get; set; } = "https://api.lingyiwanwu.com";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// 百川 提供商
/// </summary>
public sealed class BaichuanProvider : ModelProvider
{
    public override string ProviderName => "Baichuan";
    public override string BaseUrl { get; set; } = "https://api.baichuan-ai.com";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// 阶跃星辰 提供商
/// </summary>
public sealed class StepFunProvider : ModelProvider
{
    public override string ProviderName => "StepFun";
    public override string BaseUrl { get; set; } = "https://api.stepfun.com";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// 讯飞星火 提供商
/// </summary>
public sealed class SparkProvider : ModelProvider
{
    public override string ProviderName => "Spark";
    public override string BaseUrl { get; set; } = "https://spark-api-open.xf-yun.com";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// 豆包 提供商（字节跳动）
/// </summary>
public sealed class DoubaoProvider : ModelProvider
{
    public override string ProviderName => "Doubao";
    public override string BaseUrl { get; set; } = "https://ark.cn-beijing.volces.com/api";
    public override string CompatibleWith => "OpenAI";
}

/// <summary>
/// MiniMax 提供商
/// </summary>
public sealed class MiniMaxProvider : ModelProvider
{
    public override string ProviderName => "MiniMax";
    public override string BaseUrl { get; set; } = "https://api.minimax.chat";
    public override string CompatibleWith => "OpenAI";
}

public sealed class ModelProviderRegistry
{
    Dictionary<string, IModelProvider> Providers { get; } = new();

    Dictionary<string, ModelChoice> FunctionalityVsModelChoices { get; } = new();

    public void RegisterProvider(IModelProvider provider)
    {
        Providers[provider.ProviderName] = provider;
    }
}

public sealed class ModelChoice
{
    public ModelProvider Provider { get; set; } = null!;
    public string ModelId { get; set; } = string.Empty;
}

public static class ChatClientProviderExtensions
{
    public static IChatClient CreateChatClient(IHttpClientFactory httpClientFactory, ModelChoice modelChoice)
    {
        var httpClient = httpClientFactory.CreateClient();
        switch (modelChoice.Provider.CompatibleWith)
        {
            case "OpenAI":
                httpClient.BaseAddress = modelChoice.Provider.BaseUrl.EndsWith('/') ? new Uri(modelChoice.Provider.BaseUrl) : new Uri(modelChoice.Provider.BaseUrl + "/");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", modelChoice.Provider.ApiKey);
                return new OpenAICompatibleChatClient(httpClient, modelChoice.ModelId);
            case "Anthropic":
                httpClient.BaseAddress = modelChoice.Provider.BaseUrl.EndsWith('/') ? new Uri(modelChoice.Provider.BaseUrl) : new Uri(modelChoice.Provider.BaseUrl + "/");
                httpClient.DefaultRequestHeaders.Add("x-api-key", modelChoice.Provider.ApiKey);
                httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                return new AnthropicCompatibleChatClient(httpClient, modelChoice.ModelId);
            case "Gemini":
                httpClient.BaseAddress = modelChoice.Provider.BaseUrl.EndsWith('/') ? new Uri(modelChoice.Provider.BaseUrl) : new Uri(modelChoice.Provider.BaseUrl + "/");
                return new GeminiCompatibleChatClient(httpClient, modelChoice.Provider.ApiKey, modelChoice.ModelId);
            default:
                throw new NotSupportedException($"Provider {modelChoice.Provider.ProviderName} is not supported.");
        }
    }
}
