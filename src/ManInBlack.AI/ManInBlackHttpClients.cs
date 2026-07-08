namespace ManInBlack.AI;

/// <summary>
/// ManInBlack 注册的命名 HttpClient 常量。
/// </summary>
public static class ManInBlackHttpClients
{
    /// <summary>
    /// LLM <see cref="Microsoft.Extensions.AI.IChatClient"/> 专用的命名 HttpClient。
    /// <para>
    /// 独立配置:不被 host(如 Aspire <c>AddServiceDefaults</c> 注入的 <c>AddStandardResilienceHandler</c>)
    /// 默认的 30s 超时/重试管道污染——LLM 流式调用由应用层 <c>RetryMiddleware</c> 统一负责重试。
    /// </para>
    /// </summary>
    public const string ChatClient = "ManInBlack.Chat";
}
