namespace Bwarp;

public sealed record SandboxResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset ExitTime { get; init; }
    public TimeSpan RunTime { get; init; }

    public bool IsSuccess => ExitCode == 0;

    public void Deconstruct(out int exitCode, out string stdout, out string stderr)
        => (exitCode, stdout, stderr) = (ExitCode, StandardOutput, StandardError);
}
