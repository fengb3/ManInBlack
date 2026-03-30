// using Microsoft.Extensions.AI;
//
// namespace ManInBlack.AI;
//
// /// <summary>
// /// Moonshot AI (Kimi) 提供商
// /// 兼容 OpenAI API 形状
// /// </summary>
// public sealed class KimiProvider : IChatClientProvider
// {
//     private readonly IHttpClientFactory _httpClientFactory;
//     public string ProviderName => "Kimi";
//
//     private const string DefaultBaseUrl = "https://api.moonshot.cn/v1/chat/completions";
//
//     public KimiProvider(IHttpClientFactory httpClientFactory)
//     {
//         _httpClientFactory = httpClientFactory;
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             DefaultBaseUrl,
//             modelId
//         );
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId, string baseUrl)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             baseUrl,
//             modelId
//         );
//     }
// }
//
// /// <summary>
// /// 智谱 GLM 提供商
// /// 兼容 OpenAI API 形状
// /// </summary>
// public sealed class GLMProvider : IChatClientProvider
// {
//     private readonly IHttpClientFactory _httpClientFactory;
//     public string ProviderName => "GLM";
//
//     private const string DefaultBaseUrl = "https://open.bigmodel.cn/api/paas/v4/chat/completions";
//
//     public GLMProvider(IHttpClientFactory httpClientFactory)
//     {
//         _httpClientFactory = httpClientFactory;
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             DefaultBaseUrl,
//             modelId
//         );
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId, string baseUrl)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             baseUrl,
//             modelId
//         );
//     }
// }
//
// /// <summary>
// /// 阿里通义 Qwen 提供商
// /// 兼容 OpenAI API 形状
// /// </summary>
// public sealed class QwenProvider : IChatClientProvider
// {
//     private readonly IHttpClientFactory _httpClientFactory;
//     public string ProviderName => "Qwen";
//
//     private const string DefaultBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
//
//     public QwenProvider(IHttpClientFactory httpClientFactory)
//     {
//         _httpClientFactory = httpClientFactory;
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             DefaultBaseUrl,
//             modelId
//         );
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId, string baseUrl)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             baseUrl,
//             modelId
//         );
//     }
// }
//
// /// <summary>
// /// 百川 Baichuan 提供商
// /// 兼容 OpenAI API 形状
// /// </summary>
// public sealed class BaichuanProvider : IChatClientProvider
// {
//     private readonly IHttpClientFactory _httpClientFactory;
//     public string ProviderName => "Baichuan";
//
//     private const string DefaultBaseUrl = "https://api.baichuan-ai.com/v1/chat/completions";
//
//     public BaichuanProvider(IHttpClientFactory httpClientFactory)
//     {
//         _httpClientFactory = httpClientFactory;
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             DefaultBaseUrl,
//             modelId
//         );
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId, string baseUrl)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             baseUrl,
//             modelId
//         );
//     }
// }
//
// /// <summary>
// /// DeepSeek 提供商
// /// 兼容 OpenAI API 形状
// /// </summary>
// public sealed class DeepSeekProvider : IChatClientProvider
// {
//     private readonly IHttpClientFactory _httpClientFactory;
//     public string ProviderName => "DeepSeek";
//
//     private const string DefaultBaseUrl = "https://api.deepseek.com/chat/completions";
//
//     public DeepSeekProvider(IHttpClientFactory httpClientFactory)
//     {
//         _httpClientFactory = httpClientFactory;
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             DefaultBaseUrl,
//             modelId
//         );
//     }
//
//     public IChatClient CreateClient(string apiKey, string modelId, string baseUrl)
//     {
//         return new OpenAICompatibleChatClient(
//             _httpClientFactory.CreateClient(),
//             apiKey,
//             baseUrl,
//             modelId
//         );
//     }
// }
