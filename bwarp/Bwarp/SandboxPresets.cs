namespace Bwarp;

public static class SandboxPresets
{
    /// Minimal: new PID + user namespace, read-only host filesystem
    public static SandboxBuilder Minimal(string command, params string[] args) =>
        Sandbox.Run(command, args)
            .Unshare(Namespaces.User | Namespaces.Pid)
            .MountProc()
            .MountDev()
            .MountTmpfs("/tmp")
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .TryBindReadOnly("/bin", "/bin")
            .TryBindReadOnly("/etc", "/etc")
            .DieWithParent()
            .NewSession();

    /// Fully isolated: all namespaces unshared, no network, minimal filesystem
    public static SandboxBuilder FullyIsolated(string command, params string[] args) =>
        Sandbox.Run(command, args)
            .UnshareAll()
            .ClearEnvironment()
            .SetEnv("PATH", "/usr/bin:/bin")
            .MountProc()
            .MountDev()
            .MountTmpfs("/tmp")
            .MountTmpfs("/home")
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .DieWithParent()
            .NewSession();

    /// Network-only isolation: just unshare network namespace
    public static SandboxBuilder NetworkIsolated(string command, params string[] args) =>
        Sandbox.Run(command, args)
            .Unshare(Namespaces.Network | Namespaces.User)
            .DieWithParent();
}
