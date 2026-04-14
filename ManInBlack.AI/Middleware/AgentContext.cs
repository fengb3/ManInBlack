using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middleware;

/// <summary>
/// 聊天客户端中间件上下文，在中间件管道中传递请求和响应信息
/// </summary>
public class AgentContext(IServiceProvider serviceProvider)
{
    /// <summary>
    /// 服务提供者，用于依赖注入
    /// </summary>
    public IServiceProvider ServiceProvider { get; } = serviceProvider;

    /// <summary>
    /// 聊天消息列表，中间件可对其进行修改
    /// </summary>
    public IList<ChatMessage> Messages { get; set; } = [];

    /// <summary>
    /// 聊天选项，中间件可对其进行修改
    /// </summary>
    public ChatOptions? Options { get; set; }

    /// <summary>
    /// 是否为流式请求
    /// </summary>
    public bool IsStreaming { get; set; }

    /// <summary>
    /// 中间件之间共享的状态字典
    /// </summary>
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

    /// <summary>
    /// 取消令牌，用于优雅停止
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}
