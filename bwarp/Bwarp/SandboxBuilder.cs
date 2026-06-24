using System.Runtime.CompilerServices;
using System.Text;
using Bwarp.Execution;
using Bwarp.Mounts;
using Bwarp.Security;

namespace Bwarp;

public sealed class SandboxBuilder
{
    private Namespaces _namespaces = Namespaces.None;
    private bool _unshareUserTry;
    private bool _unshareCgroupTry;
    private bool _useUnshareAll;
    private bool _shareNetwork;

    private int? _uid;
    private int? _gid;
    private string? _hostname;

    private readonly List<MountEntry> _mounts = [];

    private bool _clearEnv;
    private readonly Dictionary<string, string> _setEnvVars = [];
    private readonly List<string> _unsetEnvVars = [];

    private readonly List<CapabilityAction> _capabilities = [];
    private byte[]? _seccompFilterData;

    private bool _dieWithParent;
    private bool _newSession;
    private bool _asPid1;
    private string? _workingDirectory;
    private string? _argv0;
    private string? _standardInput;

    private string _command = "";
    private readonly List<string> _arguments = [];

    private string _bwrapPath = "/usr/bin/bwrap";

    internal SandboxBuilder() { }

    // --- Namespace isolation ---

    public SandboxBuilder Unshare(Namespaces ns)
    {
        _namespaces = ns;
        _useUnshareAll = false;
        return this;
    }

    public SandboxBuilder UnshareAll()
    {
        _useUnshareAll = true;
        _namespaces = Namespaces.All;
        return this;
    }

    public SandboxBuilder UnshareUserTry()
    {
        _unshareUserTry = true;
        return this;
    }

    public SandboxBuilder UnshareCgroupTry()
    {
        _unshareCgroupTry = true;
        return this;
    }

    public SandboxBuilder ShareNetwork()
    {
        _shareNetwork = true;
        return this;
    }

    // --- Filesystem mounts (order-preserving) ---

    public SandboxBuilder Bind(string source, string dest, MountAccess access = MountAccess.ReadWrite)
    {
        _mounts.Add(new BindMount(source, dest, access));
        return this;
    }

    public SandboxBuilder BindReadOnly(string source, string dest)
    {
        _mounts.Add(new BindMount(source, dest, MountAccess.ReadOnly));
        return this;
    }

    public SandboxBuilder BindDevice(string source, string dest)
    {
        _mounts.Add(new BindMount(source, dest, MountAccess.Device));
        return this;
    }

    public SandboxBuilder TryBind(string source, string dest, MountAccess access = MountAccess.ReadWrite)
    {
        _mounts.Add(new BindMount(source, dest, access, Try: true));
        return this;
    }

    public SandboxBuilder TryBindReadOnly(string source, string dest)
    {
        _mounts.Add(new BindMount(source, dest, MountAccess.ReadOnly, Try: true));
        return this;
    }

    public SandboxBuilder TryBindDevice(string source, string dest)
    {
        _mounts.Add(new BindMount(source, dest, MountAccess.Device, Try: true));
        return this;
    }

    public SandboxBuilder MountTmpfs(string dest, int? permissions = null, long? sizeBytes = null)
    {
        _mounts.Add(new TmpfsMount(dest, permissions, sizeBytes));
        return this;
    }

    public SandboxBuilder MountProc(string dest = "/proc")
    {
        _mounts.Add(new ProcMount(dest));
        return this;
    }

    public SandboxBuilder MountDev(string dest = "/dev")
    {
        _mounts.Add(new DevMount(dest));
        return this;
    }

    public SandboxBuilder CreateDir(string dest, int? permissions = null)
    {
        _mounts.Add(new DirCreate(dest, permissions));
        return this;
    }

    public SandboxBuilder CreateSymlink(string source, string dest)
    {
        _mounts.Add(new SymlinkCreate(source, dest));
        return this;
    }

    public SandboxBuilder RemountReadOnly(string dest)
    {
        _mounts.Add(new RemountReadOnly(dest));
        return this;
    }

    /// <summary>
    /// default-deny 基线:只读绑定精选系统路径(供命令运行所需)+ proc/dev/tmp + 命名空间隔离 + die/newsession。
    /// 故意<strong>不</strong>绑定整个 "/",不绑定用户数据。调用方随后绑定 workspace(可写)与额外只读根。
    /// </summary>
    public SandboxBuilder ConfineBaseline()
    {
        return this
            .Unshare(Namespaces.User | Namespaces.Pid | Namespaces.Ipc | Namespaces.Uts)
            .UnshareCgroupTry()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .TryBindReadOnly("/bin", "/bin")
            .TryBindReadOnly("/sbin", "/sbin")
            .BindReadOnly("/etc", "/etc")
            .TryBindReadOnly("/run", "/run")
            .TryBindReadOnly("/opt", "/opt")
            .MountProc()
            .MountDev()
            .MountTmpfs("/tmp")
            .DieWithParent()
            .NewSession();
    }

    // --- Environment ---

    public SandboxBuilder ClearEnvironment()
    {
        _clearEnv = true;
        return this;
    }

    public SandboxBuilder SetEnv(string variable, string value)
    {
        _setEnvVars[variable] = value;
        return this;
    }

    public SandboxBuilder UnsetEnv(string variable)
    {
        _unsetEnvVars.Add(variable);
        return this;
    }

    // --- Identity ---

    public SandboxBuilder WithUid(int uid)
    {
        _uid = uid;
        return this;
    }

    public SandboxBuilder WithGid(int gid)
    {
        _gid = gid;
        return this;
    }

    public SandboxBuilder WithHostname(string hostname)
    {
        _hostname = hostname;
        return this;
    }

    // --- Security ---

    public SandboxBuilder AddCapability(string capability)
    {
        _capabilities.Add(CapabilityAction.Add(capability));
        return this;
    }

    public SandboxBuilder DropCapability(string capability)
    {
        _capabilities.Add(CapabilityAction.Drop(capability));
        return this;
    }

    public SandboxBuilder DropAllCapabilities()
    {
        _capabilities.Add(CapabilityAction.DropAll());
        return this;
    }

    public SandboxBuilder WithSeccompFilter(byte[] bpfData)
    {
        _seccompFilterData = bpfData;
        return this;
    }

    // --- Process behavior ---

    public SandboxBuilder DieWithParent()
    {
        _dieWithParent = true;
        return this;
    }

    public SandboxBuilder NewSession()
    {
        _newSession = true;
        return this;
    }

    public SandboxBuilder AsPid1()
    {
        _asPid1 = true;
        return this;
    }

    public SandboxBuilder WithArgv0(string argv0)
    {
        _argv0 = argv0;
        return this;
    }

    /// <summary>写入沙盒进程标准输入的内容;null 时不重定向 stdin(默认行为)。</summary>
    public SandboxBuilder WithStandardInput(string? standardInput)
    {
        _standardInput = standardInput;
        return this;
    }

    public SandboxBuilder WithWorkingDirectory(string dir)
    {
        _workingDirectory = dir;
        return this;
    }

    public SandboxBuilder WithBwrapPath(string path)
    {
        _bwrapPath = path;
        return this;
    }

    // --- Command ---

    public SandboxBuilder WithCommand(string command, params string[] args)
    {
        _command = command;
        _arguments.Clear();
        _arguments.AddRange(args);
        return this;
    }

    // --- Build ---

    public SandboxOptions Build() => new()
    {
        UnshareNamespaces = _namespaces,
        UnshareUserTry = _unshareUserTry,
        UnshareCgroupTry = _unshareCgroupTry,
        UseUnshareAll = _useUnshareAll,
        ShareNetwork = _shareNetwork,
        Uid = _uid,
        Gid = _gid,
        Hostname = _hostname,
        Mounts = _mounts.ToArray(),
        ClearEnv = _clearEnv,
        SetEnvVars = new Dictionary<string, string>(_setEnvVars),
        UnsetEnvVars = _unsetEnvVars.ToArray(),
        Capabilities = _capabilities.ToArray(),
        SeccompFilterData = _seccompFilterData,
        DieWithParent = _dieWithParent,
        NewSession = _newSession,
        AsPid1 = _asPid1,
        WorkingDirectory = _workingDirectory,
        Argv0 = _argv0,
        StandardInput = _standardInput,
        Command = _command,
        Arguments = _arguments.ToArray(),
        BwrapPath = _bwrapPath,
    };

    // --- Execute ---

    public SandboxResult Execute()
    {
        var options = Build();
        return new SandboxProcess(options).Execute();
    }

    public Task<SandboxResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var options = Build();
        return new SandboxProcess(options).ExecuteAsync(cancellationToken);
    }

    public async IAsyncEnumerable<SandboxEvent> ListenAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var options = Build();
        await foreach (var evt in new SandboxProcess(options).ListenAsync(cancellationToken))
            yield return evt;
    }
}
