using ManInBlack.AI.Abstraction.Factory;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Factory;

/// <summary>
/// Agent 工厂，根据预设配置创建 Agent 实例
/// </summary>
public class AgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDictionary<string, AgentPreset> _presets;

    /// <summary>
    /// 初始化 Agent 工厂
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="presets">预设注册表</param>
    public AgentFactory(IServiceProvider serviceProvider, IDictionary<string, AgentPreset> presets)
    {
        _serviceProvider = serviceProvider;
        _presets = presets;
    }

    /// <summary>
    /// 根据预设名称创建 Agent 实例
    /// </summary>
    /// <param name="presetName">预设名称</param>
    /// <param name="options">运行时参数</param>
    /// <returns>包含 AgentContext 和 Pipeline 的 AgentInstance</returns>
    /// <exception cref="KeyNotFoundException">当指定的预设名称不存在时抛出</exception>
    public AgentInstance Create(string presetName, AgentCreateOptions options)
    {
        if (!_presets.TryGetValue(presetName, out var preset))
            throw new KeyNotFoundException($"未找到名为 '{presetName}' 的 Agent 预设");

        // 使用 new AgentContext(serviceProvider) 而非 DI 解析，避免 Scoped 冲突
        var context = new AgentContext(_serviceProvider)
        {
            SystemPrompt = preset.Instruction,
            UserInput = options.UserInput,
            AgentId = Guid.NewGuid().ToString(),
            ParentId = options.ParentId,
            ParentType = options.ParentType,
            SessionId = options.SessionId ?? string.Empty,
            CancellationToken = options.CancellationToken
        };

        // 根据预设的 PipelineName 构建管道
        var builder = new AgentPipelineBuilder();
        switch (preset.PipelineName)
        {
            case AgentPipelineNames.Default:
                builder.UseDefault();
                break;
            case AgentPipelineNames.Simple:
                builder.UseSimple();
                break;
            default:
                throw new ArgumentException($"未知的管道名称: '{preset.PipelineName}'", nameof(preset));
        }

        // 延迟构建管道：Build() 会立即解析 IChatClient，
        // 测试场景使用 EmptyServiceProvider（无 IChatClient 注册），
        // 因此将 Build() 推迟到管道首次被调用时执行，并通过 Lazy 缓存结果
        var serviceProvider = _serviceProvider;
        var lazyPipeline = new Lazy<Func<AgentContext, IAsyncEnumerable<ChatResponseUpdate>>>(
            () => builder.Build(serviceProvider));

        return new AgentInstance
        {
            Context = context,
            Pipeline = ctx => lazyPipeline.Value(ctx)
        };
    }
}
