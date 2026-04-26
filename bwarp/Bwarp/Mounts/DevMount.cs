namespace Bwarp.Mounts;

public sealed record DevMount(string Destination = "/dev") : MountEntry
{
    public override IReadOnlyList<string> ToArguments() => ["--dev", Destination];
}
