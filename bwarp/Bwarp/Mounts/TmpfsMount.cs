namespace Bwarp.Mounts;

public sealed record TmpfsMount(
    string Destination,
    int? Permissions = null,
    long? SizeBytes = null) : MountEntry
{
    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string>();
        if (Permissions.HasValue)
        {
            args.Add("--perms");
            args.Add(Permissions.Value.ToString());
        }
        if (SizeBytes.HasValue)
        {
            args.Add("--size");
            args.Add(SizeBytes.Value.ToString());
        }
        args.Add("--tmpfs");
        args.Add(Destination);
        return args;
    }
}
