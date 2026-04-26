using Bwarp.Security;

namespace Bwarp.Execution;

internal static class BwrapArgumentBuilder
{
    public static List<string> BuildArguments(SandboxOptions options)
    {
        var args = new List<string>();

        // 1. Namespaces
        if (options.UseUnshareAll)
        {
            args.Add("--unshare-all");
        }
        else
        {
            if (options.UnshareNamespaces.HasFlag(Namespaces.User))
                args.Add(options.UnshareUserTry ? "--unshare-user-try" : "--unshare-user");
            if (options.UnshareNamespaces.HasFlag(Namespaces.Ipc))
                args.Add("--unshare-ipc");
            if (options.UnshareNamespaces.HasFlag(Namespaces.Pid))
                args.Add("--unshare-pid");
            if (options.UnshareNamespaces.HasFlag(Namespaces.Network))
                args.Add("--unshare-net");
            if (options.UnshareNamespaces.HasFlag(Namespaces.Uts))
                args.Add("--unshare-uts");
            if (options.UnshareNamespaces.HasFlag(Namespaces.Cgroup))
                args.Add(options.UnshareCgroupTry ? "--unshare-cgroup-try" : "--unshare-cgroup");
        }

        if (options.ShareNetwork)
            args.Add("--share-net");

        // 2. Identity
        if (options.Uid.HasValue) { args.Add("--uid"); args.Add(options.Uid.Value.ToString()); }
        if (options.Gid.HasValue) { args.Add("--gid"); args.Add(options.Gid.Value.ToString()); }
        if (options.Hostname is not null) { args.Add("--hostname"); args.Add(options.Hostname); }

        // 3. Process behavior
        if (options.DieWithParent) args.Add("--die-with-parent");
        if (options.NewSession) args.Add("--new-session");
        if (options.AsPid1) args.Add("--as-pid-1");

        // 4. Environment
        if (options.ClearEnv) args.Add("--clearenv");
        foreach (var (key, value) in options.SetEnvVars)
        {
            args.Add("--setenv");
            args.Add(key);
            args.Add(value);
        }
        foreach (var var in options.UnsetEnvVars)
        {
            args.Add("--unsetenv");
            args.Add(var);
        }

        // 5. Capabilities (order matters)
        foreach (var cap in options.Capabilities)
        {
            args.Add(cap.Kind == CapabilityActionKind.Add ? "--cap-add" : "--cap-drop");
            args.Add(cap.Capability);
        }

        // 6. Working directory
        if (options.WorkingDirectory is not null)
        {
            args.Add("--chdir");
            args.Add(options.WorkingDirectory);
        }

        // 7. Mounts (order critical)
        foreach (var mount in options.Mounts)
            args.AddRange(mount.ToArguments());

        // 8. argv0
        if (options.Argv0 is not null)
        {
            args.Add("--argv0");
            args.Add(options.Argv0);
        }

        // 9. Command separator and command
        args.Add("--");
        args.Add(options.Command);
        args.AddRange(options.Arguments);

        return args;
    }
}
