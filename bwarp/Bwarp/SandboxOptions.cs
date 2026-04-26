using Bwarp.Mounts;
using Bwarp.Security;

namespace Bwarp;

public sealed record SandboxOptions
{
    public Namespaces UnshareNamespaces { get; init; } = Namespaces.None;
    public bool UnshareUserTry { get; init; }
    public bool UnshareCgroupTry { get; init; }
    public bool UseUnshareAll { get; init; }
    public bool ShareNetwork { get; init; }

    public int? Uid { get; init; }
    public int? Gid { get; init; }
    public string? Hostname { get; init; }

    public IReadOnlyList<MountEntry> Mounts { get; init; } = [];

    public bool ClearEnv { get; init; }
    public IReadOnlyDictionary<string, string> SetEnvVars { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> UnsetEnvVars { get; init; } = [];

    public IReadOnlyList<CapabilityAction> Capabilities { get; init; } = [];
    public byte[]? SeccompFilterData { get; init; }

    public bool DieWithParent { get; init; }
    public bool NewSession { get; init; }
    public bool AsPid1 { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? Argv0 { get; init; }

    public string Command { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string BwrapPath { get; init; } = "/usr/bin/bwrap";
}
