using System.Diagnostics;
using System.IO;
using System.Text;
using ManInBlack.AI.Abstraction.Tools;

namespace ManInBlack.AI.Services;

/// <summary>
/// 基于 Process.Start 的 Shell 执行器，用于 Windows 和 macOS
/// </summary>
public class ProcessShellExecutor : IShellExecutor
{
    public ShellResult Execute(string command, string workingDirectory, int timeoutMs, string? stdin = null)
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
        if (stdin is not null)
        {
            processInfo.RedirectStandardInput = true;
            // 无 BOM 的 UTF8:Encoding.UTF8 带 BOM preamble,StreamWriter 首次 Write 会把 BOM 写进 stdin,
            // 导致脚本侧 json.load(sys.stdin) 因首字节 BOM 解析失败。stdin 用不 emit identifier 的 UTF8。
            processInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        processInfo.ArgumentList.Add("-c");
        processInfo.ArgumentList.Add(command);

        var process = Process.Start(processInfo);
        if (process is null)
            return new ShellResult { ExitCode = -1, StandardError = "Failed to start Bash process." };

        // 并发读 out/err(消除同步 ReadToEnd 的死锁隐患);写完 stdin 后 Close 发 EOF。
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (stdin is not null)
        {
            try
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // 子进程已退出 / 管道断开(如超时 kill),忽略;结果由退出码体现。
            }
        }

        string output;
        string error;

        if (!process.WaitForExit(TimeSpan.FromMilliseconds(timeoutMs)))
        {
            process.Kill();
            process.WaitForExit();
            output = outputTask.Result;
            error = errorTask.Result;
            process.Dispose();
            return new ShellResult { ExitCode = -1, TimedOut = true, StandardOutput = output, StandardError = error };
        }

        output = outputTask.Result;
        error = errorTask.Result;
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
