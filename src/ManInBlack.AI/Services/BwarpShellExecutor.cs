using Bwarp;
using ManInBlack.AI.Abstraction.Tools;

namespace ManInBlack.AI.Services;

/// <summary>
/// 基于 Bwarp (bubblewrap) 沙盒的 Shell 执行器，用于 Linux
/// </summary>
public class BwarpShellExecutor : IShellExecutor
{
    public ShellResult Execute(string command, string workingDirectory, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            var result = Sandbox.Confine(workingDirectory, command)
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
