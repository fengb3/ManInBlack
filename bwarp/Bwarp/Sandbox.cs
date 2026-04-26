namespace Bwarp;

public static class Sandbox
{
    public static SandboxBuilder Run(string command, params string[] args)
    {
        return new SandboxBuilder().WithCommand(command, args);
    }

    /// <summary>
    /// Confine a shell command to a working directory. The command runs in a sandbox
    /// where the entire host filesystem is read-only, except the specified directory
    /// which is writable. Network access is allowed.
    /// </summary>
    public static SandboxBuilder Confine(string workingDirectory, string command)
    {
        return new SandboxBuilder()
            .WithCommand("/bin/bash", "-c", command)
            .Unshare(Namespaces.User | Namespaces.Pid | Namespaces.Ipc | Namespaces.Uts)
            .UnshareCgroupTry()
            .BindReadOnly("/", "/")
            .Bind(workingDirectory, workingDirectory)
            .MountProc()
            .MountDev()
            .MountTmpfs("/tmp")
            .DieWithParent()
            .NewSession()
            .WithWorkingDirectory(workingDirectory);
    }
}
