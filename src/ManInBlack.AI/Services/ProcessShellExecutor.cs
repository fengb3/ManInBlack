using System.Diagnostics;
using System.Text;
using ManInBlack.AI.Abstraction.Tools;

namespace ManInBlack.AI.Services;

/// <summary>
/// 基于 Process.Start 的 Shell 执行器，用于 Windows 和 macOS
/// </summary>
public class ProcessShellExecutor : IShellExecutor
{
    public ShellResult Execute(string command, string workingDirectory, int timeoutMs)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = FindBashExecutable(),
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        processInfo.ArgumentList.Add("-c");
        processInfo.ArgumentList.Add(command);

        var process = Process.Start(processInfo);
        if (process is null)
            return new ShellResult { ExitCode = -1, StandardError = "Failed to start Bash process." };

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(TimeSpan.FromMilliseconds(timeoutMs)))
        {
            process.Kill();
            process.WaitForExit();
            process.Dispose();
            return new ShellResult { ExitCode = -1, TimedOut = true };
        }

        var exitCode = process.ExitCode;
        process.Dispose();
        return new ShellResult
        {
            ExitCode = exitCode,
            StandardOutput = output,
            StandardError = error,
        };
    }

    private static string FindBashExecutable()
    {
        if (!OperatingSystem.IsWindows()) return "bash";

        var gitBash = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Git", "bin", "bash.exe");
        return File.Exists(gitBash) ? gitBash : "bash";
    }
}
