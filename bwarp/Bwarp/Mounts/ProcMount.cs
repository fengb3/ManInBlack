namespace Bwarp.Mounts;

public sealed record ProcMount(string Destination = "/proc") : MountEntry
{
    public override IReadOnlyList<string> ToArguments() => ["--proc", Destination];
}
