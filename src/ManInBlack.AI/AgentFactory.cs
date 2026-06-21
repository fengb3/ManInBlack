using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI;

/// <summary>
/// Agent 工厂，负责管理 Agent 定义、管道配置和执行跟踪
/// </summary>
public class AgentFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentFactory> _logger;

    /// <summary>
    /// 已注册的 Agent 定义
    /// </summary>
    private readonly ConcurrentDictionary<string, AgentDefinition> _definitions = new();

    /// <summary>
    /// 已注册的管道配置委托
    /// </summary>
    private readonly ConcurrentDictionary<string, Func<AgentPipelineBuilder, AgentPipelineBuilder>> _pipelineResolvers = new();

    /// <summary>
    /// 正在执行的 Agent 跟踪，用于在新请求到来时取消旧 Agent
    /// </summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tracking = new();

    /// <summary>
    /// Agent 实例到根用户 ID 的映射，用于子 Agent 追溯到最初发起请求的用户
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _agentToRootUser = new();

    public AgentFactory(
        IServiceScopeFactory scopeFactory,
        ILogger<AgentFactory> logger,
        IEnumerable<AgentDefinition> definitions,
        IEnumerable<PipelineRegistration> pipelines)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        foreach (var def in definitions)
            RegisterDefinition(def);

        // 内置管道预设
        _pipelineResolvers["default"] = builder => builder.UseDefault();
        _pipelineResolvers["simple"] = builder => builder.UseSimple();

        // 收集 builder 期（.AddPipeline）注册的 pipeline，覆盖同名内置
        foreach (var reg in pipelines)
            _pipelineResolvers[reg.Name] = reg.Resolver;
    }

    /// <summary>
    /// 注册 Agent 定义。如果同名 Agent 已存在，抛出 <see cref="ArgumentException"/>
    /// </summary>
    public void RegisterDefinition(AgentDefinition definition)
    {
        if (!_definitions.TryAdd(definition.Name, definition))
            throw new ArgumentException($"已存在同名 Agent 定义：{definition.Name}", nameof(definition));
    }

    /// <summary>
    /// 获取指定名称的 Agent 定义。不存在时抛出 <see cref="KeyNotFoundException"/>
    /// </summary>
    public AgentDefinition GetDefinition(string name)
    {
        return _definitions.TryGetValue(name, out var definition)
            ? definition
            : throw new KeyNotFoundException($"未找到 Agent 定义：{name}");
    }

    /// <summary>
    /// 获取所有已注册的 Agent 定义的只读集合
    /// </summary>
    public IReadOnlyCollection<AgentDefinition> Definitions => (IReadOnlyCollection<AgentDefinition>)_definitions.Values;

    /// <summary>
    /// 注册管道配置委托
    /// </summary>
    /// <param name="name">管道名称</param>
    /// <param name="configure">配置委托，接收 <see cref="AgentPipelineBuilder"/> 并返回配置后的 builder</param>
    public void RegisterPipeline(string name, Func<AgentPipelineBuilder, AgentPipelineBuilder> configure)
    {
        _pipelineResolvers[name] = configure;
    }

    /// <summary>
    /// 取消该用户现有的 Agent（如果有），注册并返回新的 CancellationTokenSource
    /// </summary>
    public CancellationTokenSource RegisterAndCancelExisting(string userId)
    {
        var newCts = new CancellationTokenSource();

        _tracking.AddOrUpdate(
            userId,
            _ => newCts,
            (_, existingCts) =>
            {
                _logger.LogInformation("取消用户 {UserId} 的正在运行的 Agent", userId);
                existingCts.Cancel();
                return newCts;
            }
        );

        return newCts;
    }

    /// <summary>
    /// 释放该用户的跟踪记录并 Dispose CTS。仅当字典中存储的 CTS 与传入的是同一实例时才移除，防止误删新 Agent 的 CTS。
    /// 无论 TryRemove 是否成功都会 Dispose CTS，避免资源泄漏。
    /// </summary>
    public void Release(string userId, CancellationTokenSource cts)
    {
        _tracking.TryRemove(new KeyValuePair<string, CancellationTokenSource>(userId, cts));
        cts.Dispose();
    }

    /// <summary>
    /// 运行指定 Agent，返回流式响应更新
    /// </summary>
    /// <param name="agentName">Agent 名称</param>
    /// <param name="userInput">用户输入文本</param>
    /// <param name="parentId">父级标识（通常是用户 ID）</param>
    /// <param name="parentType">父级类型（例如 "feishu_user"）</param>
    /// <param name="configure">可选回调，让调用者微调 AgentContext</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>流式响应更新</returns>
    public async IAsyncEnumerable<ChatResponseUpdate> RunAsync(
        string agentName,
        string userInput,
        string parentId,
        string parentType,
        Action<AgentContext>? configure = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. 获取 Agent 定义
        var definition = GetDefinition(agentName);

        // 2. 创建 DI scope（在 finally 中释放）
        var scope = _scopeFactory.CreateScope();

        try
        {
            var sp = scope.ServiceProvider;

            // 确保 MCP server 已连接（兼容 BuildServiceProvider 的 console 场景；IHost 应用由 HostedService 先触发，幂等）
            await sp.GetRequiredService<McpClientHostedService>().EnsureStartedAsync(ct);

            // 3. 解析依赖服务
            var userStorage = sp.GetRequiredService<IUserStorage>();
            var agentContext = sp.GetRequiredService<AgentContext>();
            // 4. 解析根用户 ID（向上追溯父级链直到 ParentType 为 "User"）
            var rootUserId = parentType == "User"
                ? parentId
                : _agentToRootUser.TryGetValue(parentId, out var root)
                    ? root
                    : parentId;

            // 5. 获取或创建根用户
            var user = await userStorage.GetOrCreateUser(rootUserId);

            // 6. 设置 AgentContext 属性
            agentContext.AgentId = Guid.NewGuid().ToString();
            agentContext.ParentId = parentId;
            agentContext.ParentType = parentType;
            agentContext.RootUserId = rootUserId;
            agentContext.SessionId = user.GetLatestSessionId() ?? await userStorage.CreateNewSessionIdAsync(rootUserId);
            agentContext.SystemPrompt = definition.Instruction;
            agentContext.UserInput = userInput;
            agentContext.AgentName = definition.Name;
            agentContext.CancellationToken = ct;

            // 7. 调用可选的微调回调
            configure?.Invoke(agentContext);

            // 7.5 解析 per-agent ModelChoice（若指定），存入 Items 供管道使用
            if (!string.IsNullOrEmpty(definition.ModelChoiceName))
            {
                var mibSettings = sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value;
                agentContext.Items["ModelChoice"] = mibSettings.GetModelChoice(definition.ModelChoiceName);
            }

            // 8. 注册到父子关系映射，供子 Agent 向上追溯（在 configure 之后，用最终的 AgentId）
            _agentToRootUser[agentContext.AgentId] = rootUserId;

            // 9. 获取管道委托，构建管道
            var pipelineName = definition.PipelineName;
            if (!_pipelineResolvers.TryGetValue(pipelineName, out var pipelineConfigure))
                throw new KeyNotFoundException($"未找到管道配置：{pipelineName}");

            var pipeline = pipelineConfigure(new AgentPipelineBuilder()).Build(sp);

            // 10. 执行管道并 yield return 每个更新
            var updates = pipeline(agentContext);
            await foreach (var update in updates.WithCancellation(ct))
            {
                yield return update;
            }
        }
        finally
        {
            // 11. 清理父子关系映射，释放 scope
            scope.Dispose();
        }
    }
}
