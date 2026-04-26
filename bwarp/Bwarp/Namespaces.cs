namespace Bwarp;

[Flags]
public enum Namespaces
{
    None = 0,
    User = 1 << 0,
    Ipc = 1 << 1,
    Pid = 1 << 2,
    Network = 1 << 3,
    Uts = 1 << 4,
    Cgroup = 1 << 5,

    All = User | Ipc | Pid | Network | Uts | Cgroup,
    Minimal = User | Pid,
    NetworkIsolated = User | Pid | Network | Uts,
}
