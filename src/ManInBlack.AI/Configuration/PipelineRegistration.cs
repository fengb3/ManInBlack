using ManInBlack.AI.Middlewares;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 一条 pipeline 注册：名称 + 构建 AgentPipelineBuilder 的委托。
/// 由 .AddPipeline 即时注册为单例，AgentFactory 构造时收集。
/// 该 record 为 public（而非 internal），是因为 AgentFactory 的 public 构造函数接收
/// IEnumerable&lt;PipelineRegistration&gt;（CS0051 一致性约束）。
/// </summary>
public sealed record PipelineRegistration(
    string Name,
    Func<AgentPipelineBuilder, AgentPipelineBuilder> Resolver);
