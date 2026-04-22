using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Core.Middleware;

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
    /// Agent 标识，唯一标识一个 Agent 实例，通常是一个 GUID 字符串
    /// </summary>
    public string AgentId { get; set; } = string.Empty;
    
    /// <summary>
    /// Agent 的父级标识，如果有的话，表示这个 Agent 是由哪个实体（用户或另一个 Agent）触发的
    /// </summary>
    public string ParentId { get; set; } = string.Empty;
    
    /// <summary>
    /// 会话标识，表示这个 Agent 所属的对话会话，可以用来关联多个 Agent 实例到同一个对话上下文
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Agent 的父级类型，例如 "User" 或 "Agent"，用于区分 ParentId 是用户还是另一个 Agent
    /// </summary>
    public string ParentType { get; set; } = string.Empty;
    
    /// <summary>
    /// 系统提示词
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;
    
    /// <summary>
    /// 本轮用户输入的原始文本，供中间件参考，但不一定会直接发送给模型
    /// </summary>
    public string UserInput { get; set; } = string.Empty;

    /// <summary>
    /// 聊天消息列表，中间件可对其进行修改
    /// </summary>
    public IList<ChatMessage> Messages { get; set; } = [];

    /// <summary>
    /// 聊天选项，中间件可对其进行修改
    /// </summary>
    public ChatOptions? Options { get; set; }

    /// <summary>
    /// 中间件之间共享的状态字典
    /// </summary>
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

    /// <summary>
    /// 累积的 token 用量，由 AgentLoopMiddleware 从流式响应中提取
    /// </summary>
    public UsageDetails AccumulatedUsage { get; } = new();

    /// <summary>
    /// 取消令牌，用于优雅停止
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}
