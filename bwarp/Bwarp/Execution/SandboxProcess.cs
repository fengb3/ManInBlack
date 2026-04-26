using System.Diagnostics;
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

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return psi;
    }
}
