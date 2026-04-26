namespace Bwarp.Mounts;

public sealed record BindMount(
    string Source,
    string Destination,
    MountAccess Access = MountAccess.ReadWrite,
    bool Try = false) : MountEntry
{
    public override IReadOnlyList<string> ToArguments()
    {
        var flag = (Access, Try) switch
        {
            (MountAccess.ReadWrite, false) => "--bind",
            (MountAccess.ReadWrite, true) => "--bind-try",
            (MountAccess.ReadOnly, false) => "--ro-bind",
            (MountAccess.ReadOnly, true) => "--ro-bind-try",
            (MountAccess.Device, false) => "--dev-bind",
            (MountAccess.Device, true) => "--dev-bind-try",
            _ => "--bind",
        };
        return [flag, Source, Destination];
    }
}
