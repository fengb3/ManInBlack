namespace Bwarp.Mounts;

public sealed record RemountReadOnly(string Destination) : MountEntry
{
    public override IReadOnlyList<string> ToArguments() => ["--remount-ro", Destination];
}
