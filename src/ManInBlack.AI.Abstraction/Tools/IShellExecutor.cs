namespace ManInBlack.AI.Abstraction.Tools;

/// <summary>
/// Shell 命令执行结果
/// </summary>
public sealed record ShellResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public bool TimedOut { get; init; }
    public bool IsSuccess => ExitCode == 0;
}

/// <summary>
/// Shell 命令执行器接口，抽象不同平台下的命令执行方式
/// </summary>
public interface IShellExecutor
{
    /// <summary>
    /// 执行 bash 命令
    /// </summary>
    /// <param name="command">要执行的命令</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="timeoutMs">超时时间（毫秒）</param>
    /// <returns>执行结果</returns>
    ShellResult Execute(string command, string workingDirectory, int timeoutMs);
}
