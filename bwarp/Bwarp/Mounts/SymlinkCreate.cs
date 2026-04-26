namespace Bwarp.Mounts;

public sealed record SymlinkCreate(string Source, string Destination) : MountEntry
{
    public override IReadOnlyList<string> ToArguments() => ["--symlink", Source, Destination];
}
