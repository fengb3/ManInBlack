namespace Bwarp.Mounts;

public sealed record DirCreate(string Destination, int? Permissions = null) : MountEntry
{
    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string>();
        if (Permissions.HasValue)
        {
            args.Add("--perms");
            args.Add(Permissions.Value.ToString());
        }
        args.Add("--dir");
        args.Add(Destination);
        return args;
    }
}
