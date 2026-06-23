using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Bwarp.Execution;

internal sealed class SandboxProcess(SandboxOptions options)
{
    public SandboxResult Execute()
    {
        var args = BwrapArgumentBuilder.BuildArguments(options);
        var startInfo = CreateStartInfo(args);

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrBuilder.AppendLine(e.Data); };

        var startTime = DateTimeOffset.UtcNow;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        WriteStandardInput(process);
        process.WaitForExit();
        var exitTime = DateTimeOffset.UtcNow;

        return new SandboxResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdoutBuilder.ToString(),
            StandardError = stderrBuilder.ToString(),
            StartTime = startTime,
            ExitTime = exitTime,
            RunTime = exitTime - startTime,
        };
    }

    public async Task<SandboxResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var args = BwrapArgumentBuilder.BuildArguments(options);
        var startInfo = CreateStartInfo(args);

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrBuilder.AppendLine(e.Data); };

        var startTime = DateTimeOffset.UtcNow;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        WriteStandardInput(process);

        await process.WaitForExitAsync(cancellationToken);
        var exitTime = DateTimeOffset.UtcNow;

        return new SandboxResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdoutBuilder.ToString(),
            StandardError = stderrBuilder.ToString(),
            StartTime = startTime,
            ExitTime = exitTime,
            RunTime = exitTime - startTime,
        };
    }

    public async IAsyncEnumerable<SandboxEvent> ListenAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var args = BwrapArgumentBuilder.BuildArguments(options);
        var startInfo = CreateStartInfo(args);

        using var process = new Process { StartInfo = startInfo };
        var channel = Channel.CreateUnbounded<SandboxEvent>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                channel.Writer.TryWrite(new SandboxEvent.StandardOutputReceived(e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                channel.Writer.TryWrite(new SandboxEvent.StandardErrorReceived(e.Data));
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        WriteStandardInput(process);

        yield return new SandboxEvent.Started(process.Id);

        try
        {
            while (!process.HasExited)
            {
                if (channel.Reader.TryRead(out var evt))
                {
                    yield return evt;
                }
                else
                {
                    await Task.Delay(50, cancellationToken);
                }
            }

            while (channel.Reader.TryRead(out var remaining))
                yield return remaining;
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        yield return new SandboxEvent.Exited(process.ExitCode);
    }

    private void WriteStandardInput(Process process)
    {
        if (options.StandardInput is null) return;
        try
        {
            process.StandardInput.Write(options.StandardInput);
            process.StandardInput.Close(); // 发 EOF,使脚本侧 json.load(sys.stdin) 读完完整输入后返回
        }
        catch (IOException)
        {
            // 子进程已退出 / 管道断开(如超时 kill、脚本未读 stdin 即退出);忽略,结果由退出码体现。
        }
    }

    private ProcessStartInfo CreateStartInfo(List<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = options.BwrapPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (options.StandardInput is not null)
        {
            psi.RedirectStandardInput = true;
            // 无 BOM 的 UTF8:避免 StreamWriter 把 BOM 写进沙盒进程 stdin,致脚本 json.load 失败。
            psi.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return psi;
    }
}
