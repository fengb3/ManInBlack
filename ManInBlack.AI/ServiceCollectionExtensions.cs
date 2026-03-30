// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Http;
//
// namespace ManInBlack.AI;
//
// /// <summary>
// /// 依赖注入扩展方法
// /// </summary>
// public static class ServiceCollectionExtensions
// {
//     /// <summary>
//     /// 添加聊天客户端服务到 DI 容器
//     /// </summary>
//     /// <param name="services">服务集合</param>
//     /// <returns>服务集合</returns>
//     public static IServiceCollection AddChatClients(this IServiceCollection services)
//     {
//         // 注册 HttpClient（必需）
//         services.AddHttpClient();
//
//         // 注册 ChatClientFactory
//         services.AddSingleton<ChatClientFactory>();
//
//         // 注册基础提供商
//         services.AddSingleton<OpenAIProvider>();
//         services.AddSingleton<AnthropicProvider>();
//         services.AddSingleton<GeminiProvider>();
//
//         return services;
//     }
//
//     /// <summary>
//     /// 添加所有国产大模型提供商
//     /// </summary>
//     /// <param name="services">服务集合</param>
//     /// <returns>服务集合</returns>
//     public static IServiceCollection AddChineseProviders(this IServiceCollection services)
//     {
//         // 注册国产模型提供商
//         services.AddSingleton<KimiProvider>();
//         services.AddSingleton<GLMProvider>();
//         services.AddSingleton<QwenProvider>();
//         services.AddSingleton<BaichuanProvider>();
//         services.AddSingleton<DeepSeekProvider>();
//
//         // 创建一个扩展的 ChatClientFactory
//         services.AddSingleton(sp =>
//         {
//             var factory = new ChatClientFactory(sp.GetRequiredService<IHttpClientFactory>());
//
//             factory.AddProvider("kimi", sp.GetRequiredService<KimiProvider>());
//             factory.AddProvider("glm", sp.GetRequiredService<GLMProvider>());
//             factory.AddProvider("qwen", sp.GetRequiredService<QwenProvider>());
//             factory.AddProvider("baichuan", sp.GetRequiredService<BaichuanProvider>());
//             factory.AddProvider("deepseek", sp.GetRequiredService<DeepSeekProvider>());
//
//             return factory;
//         });
//
//         return services;
//     }
//
//     /// <summary>
//     /// 添加命名 HttpClient 用于聊天客户端
//     /// </summary>
//     /// <param name="services">服务集合</param>
//     /// <param name="httpClientName">HttpClient 名称</param>
//     /// <param name="configureAction">配置委托</param>
//     /// <returns>服务集合</returns>
//     public static IServiceCollection AddChatHttpClient(
//         this IServiceCollection services,
//         string httpClientName,
//         Action<IHttpClientBuilder>? configureAction = null)
//     {
//         var builder = services.AddHttpClient(httpClientName);
//         configureAction?.Invoke(builder);
//         return services;
//     }
// }
