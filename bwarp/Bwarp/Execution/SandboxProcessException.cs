namespace Bwarp.Execution;

public sealed class SandboxProcessException : Exception
{
    public int ExitCode { get; }
    public string StandardError { get; }
    public string StandardOutput { get; }

    public SandboxProcessException(int exitCode, string stderr, string stdout)
        : base($"Sandbox process exited with code {exitCode}: {stderr}")
    {
        ExitCode = exitCode;
        StandardError = stderr;
        StandardOutput = stdout;
    }
}
