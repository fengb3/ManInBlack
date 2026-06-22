using Bwarp;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;

namespace ManInBlack.AI.Services;

/// <summary>
/// 基于 Bwarp (bubblewrap) 沙盒的 Shell 执行器,用于 Linux。
/// 隔离由 FileAccessPolicy 驱动:只暴露 workspace(可写)+ 配置只读根 + 精选系统路径。
/// </summary>
public class BwarpShellExecutor(FileAccessPolicy policy) : IShellExecutor
{
    public ShellResult Execute(string command, string workingDirectory, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            var result = Sandbox.Confine(policy.Workspace, command, policy.ReadableRoots)
                .ExecuteAsync(cts.Token)
                .GetAwaiter()
                .GetResult();

            return new ShellResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
            };
        }
        catch (OperationCanceledException)
        {
            return new ShellResult { ExitCode = -1, TimedOut = true };
        }
    }
}
