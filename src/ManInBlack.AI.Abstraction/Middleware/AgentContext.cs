using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Middleware;

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
    /// Agent 定义名称，对应 AgentDefinition.Name
    /// </summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>
    /// Agent 标识，唯一标识一个 Agent 实例，通常是一个 GUID 字符串
    /// </summary>
    public string AgentId { get; set; } = string.Empty;
    
    /// <summary>
    /// 会话标识，表示这个 Agent 所属的对话会话，可以用来关联多个 Agent 实例到同一个对话上下文
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Agent 的父级类型，例如 "User" 或 "Agent"，用于区分 ParentId 是用户还是另一个 Agent
    /// </summary>
    public string ParentType { get; set; } = string.Empty;

    /// <summary>
    /// 根用户 ID，用于子 Agent 追溯到最初发起请求的用户。
    /// 当 ParentType 为 "User" 时等于 ParentId；为 "Agent" 时由 AgentFactory 向上解析。
    /// </summary>
    public string RootUserId { get; set; } = string.Empty;

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

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
