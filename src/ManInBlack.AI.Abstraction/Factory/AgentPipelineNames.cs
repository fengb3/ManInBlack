namespace ManInBlack.AI.Abstraction.Factory;

/// <summary>
/// 预定义管道名称常量
/// </summary>
public static class AgentPipelineNames
{
    /// <summary>默认管道，包含完整的中间件链</summary>
    public const string Default = "Default";

    /// <summary>简化管道，仅包含核心中间件</summary>
    public const string Simple = "Simple";
}