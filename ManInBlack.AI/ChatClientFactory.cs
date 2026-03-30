// using Microsoft.Extensions.AI;
//
// namespace ManInBlack.AI;
//
// /// <summary>
// /// 模型配置
// /// </summary>
// public sealed record ModelConfig
// {
//     /// <summary>
//     /// 提供商名称
//     /// </summary>
//     public required string Provider { get; init; }
//
//     /// <summary>
//     /// 模型 ID
//     /// </summary>
//     public required string ModelId { get; init; }
//
//     /// <summary>
//     /// API 密钥
//     /// </summary>
//     public required string ApiKey { get; init; }
//
//     /// <summary>
//     /// 自定义基础地址（可选）
//     /// </summary>
//     public string? BaseUrl { get; init; }
// }
//
// /// <summary>
// /// 聊天客户端工厂，支持运行时动态切换提供商和模型
// /// </summary>
// public sealed class ChatClientFactory
// {
//     private readonly IHttpClientFactory _httpClientFactory;
//     private readonly Dictionary<string, IChatClientProvider> _providers;
//
//     public ChatClientFactory(IHttpClientFactory httpClientFactory)
//     {
//         _httpClientFactory = httpClientFactory;
//         _providers = new Dictionary<string, IChatClientProvider>(StringComparer.OrdinalIgnoreCase)
//         {
//             ["openai"] = new OpenAIProvider(_httpClientFactory),
//             ["anthropic"] = new AnthropicProvider(_httpClientFactory),
//             ["gemini"] = new GeminiProvider(_httpClientFactory)
//         };
//     }
//
//     /// <summary>
//     /// 创建聊天客户端
//     /// </summary>
//     /// <param name="provider">提供商名称（openai, anthropic, gemini）</param>
//     /// <param name="apiKey">API 密钥</param>
//     /// <param name="modelId">模型 ID</param>
//     /// <returns>IChatClient 实例</returns>
//     public IChatClient CreateClient(string provider, string apiKey, string modelId)
//     {
//         if (!_providers.TryGetValue(provider, out var providerInstance))
//         {
//             throw new ArgumentException($"Unknown provider: {provider}. Available providers: {string.Join(", ", _providers.Keys)}");
//         }
//
//         return providerInstance.CreateClient(apiKey, modelId);
//     }
//
//     /// <summary>
//     /// 创建聊天客户端（使用自定义端点）
//     /// </summary>
//     /// <param name="provider">提供商名称（openai, anthropic, gemini）</param>
//     /// <param name="apiKey">API 密钥</param>
//     /// <param name="modelId">模型 ID</param>
//     /// <param name="baseUrl">自定义基础地址</param>
//     /// <returns>IChatClient 实例</returns>
//     public IChatClient CreateClient(string provider, string apiKey, string modelId, string baseUrl)
//     {
//         if (!_providers.TryGetValue(provider, out var providerInstance))
//         {
//             throw new ArgumentException($"Unknown provider: {provider}. Available providers: {string.Join(", ", _providers.Keys)}");
//         }
//
//         return providerInstance.CreateClient(apiKey, modelId, baseUrl);
//     }
//
//     /// <summary>
//     /// 从配置创建聊天客户端
//     /// </summary>
//     /// <param name="config">模型配置</param>
//     /// <returns>IChatClient 实例</returns>
//     public IChatClient CreateClient(ModelConfig config)
//     {
//         if (config.BaseUrl is not null)
//         {
//             return CreateClient(config.Provider, config.ApiKey, config.ModelId, config.BaseUrl);
//         }
//         return CreateClient(config.Provider, config.ApiKey, config.ModelId);
//     }
//
//     /// <summary>
//     /// 添加自定义提供商
//     /// </summary>
//     /// <param name="name">提供商名称</param>
//     /// <param name="provider">提供商实例</param>
//     public void AddProvider(string name, IChatClientProvider provider)
//     {
//         _providers[name] = provider;
//     }
//
//     /// <summary>
//     /// 获取所有可用的提供商名称
//     /// </summary>
//     public IReadOnlyCollection<string> GetAvailableProviders()
//     {
//         return _providers.Keys.ToList().AsReadOnly();
//     }
// }
//
// /// <summary>
// /// 动态聊天客户端，运行时可切换配置
// /// </summary>
// public sealed class DynamicChatClient : IChatClient
// {
//     private readonly ChatClientFactory _factory;
//     private ModelConfig _currentConfig;
//     private IChatClient _currentClient;
//
//     /// <summary>
//     /// 当前使用的配置
//     /// </summary>
//     public ModelConfig CurrentConfig => _currentConfig;
//
//     /// <summary>
//     /// 创建动态聊天客户端
//     /// </summary>
//     /// <param name="factory">聊天客户端工厂</param>
//     /// <param name="initialConfig">初始配置</param>
//     public DynamicChatClient(ChatClientFactory factory, ModelConfig initialConfig)
//     {
//         _factory = factory;
//         _currentConfig = initialConfig;
//         _currentClient = _factory.CreateClient(initialConfig);
//     }
//
//     /// <summary>
//     /// 切换到新的配置
//     /// </summary>
//     /// <param name="newConfig">新配置</param>
//     public void SwitchTo(ModelConfig newConfig)
//     {
//         _currentConfig = newConfig;
//         _currentClient = _factory.CreateClient(newConfig);
//     }
//
//     /// <summary>
//     /// 切换到新的提供商和模型
//     /// </summary>
//     /// <param name="provider">提供商名称</param>
//     /// <param name="apiKey">API 密钥</param>
//     /// <param name="modelId">模型 ID</param>
//     public void SwitchTo(string provider, string apiKey, string modelId)
//     {
//         SwitchTo(new ModelConfig
//         {
//             Provider = provider,
//             ApiKey = apiKey,
//             ModelId = modelId
//         });
//     }
//
//     public Task<ChatResponse> GetResponseAsync(
//         IEnumerable<ChatMessage> messages,
//         ChatOptions? options = null,
//         CancellationToken cancellationToken = default)
//     {
//         return _currentClient.GetResponseAsync(messages, options, cancellationToken);
//     }
//
//     public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
//         IEnumerable<ChatMessage> messages,
//         ChatOptions? options = null,
//         CancellationToken cancellationToken = default)
//     {
//         return _currentClient.GetStreamingResponseAsync(messages, options, cancellationToken);
//     }
//
//     public void Dispose()
//     {
//         _currentClient.Dispose();
//     }
//
//     public object? GetService(Type serviceType, object? serviceKey = null)
//     {
//         return _currentClient.GetService(serviceType, serviceKey);
//     }
// }
