using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Agent;

/// <summary>
/// Agent 执行结果
/// </summary>
public sealed class AgentResult
{
    /// <summary>
    /// Agent 输出的文本内容
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// Token 用量详情
    /// </summary>
    public UsageDetails Usage { get; set; } = new();

    /// <summary>
    /// 是否执行成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 执行失败时的异常信息
    /// </summary>
    public Exception? Error { get; set; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static AgentResult Ok(string output, UsageDetails usage) => new()
    {
        Output = output,
        Usage = usage,
        Success = true
    };

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static AgentResult Fail(Exception ex) => new()
    {
        Success = false,
        Error = ex
    };
}
