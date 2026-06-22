using Bwarp;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;

namespace ManInBlack.AI.Services;

/// <summary>
/// 基于 Bwarp (bubblewrap) 沙盒的 Shell 执行器,用于 Linux。
/// 可写目录为调用方传入的 workingDirectory(IShellExecutor 契约):CommandLineTools 传用户 workspace,
/// HookExecutor 传 hooks/ 或 workspace。FileAccessPolicy 仅提供 ReadableRoots(额外只读根);
/// 精选系统路径由 Sandbox.Confine 的 baseline 默认只读挂载。其他一切默认不可见。
/// </summary>
public class BwarpShellExecutor(FileAccessPolicy policy) : IShellExecutor
{
    public ShellResult Execute(string command, string workingDirectory, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            // 可写目录 = 调用方 workingDirectory(IShellExecutor 契约);不用 policy.Workspace,
            // 否则全局钩子({RootPath}/hooks/)会丢失脚本目录与 CWD。
            var result = Sandbox.Confine(workingDirectory, command, policy.ReadableRoots)
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
