namespace Bwarp.Mounts;

public abstract record MountEntry
{
    public abstract IReadOnlyList<string> ToArguments();
}
