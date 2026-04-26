namespace Bwarp;

public abstract record SandboxEvent
{
    public sealed record Started(int ProcessId) : SandboxEvent;
    public sealed record StandardOutputReceived(string Text) : SandboxEvent;
    public sealed record StandardErrorReceived(string Text) : SandboxEvent;
    public sealed record Exited(int ExitCode) : SandboxEvent;
}
