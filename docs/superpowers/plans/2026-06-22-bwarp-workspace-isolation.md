# bwarp + FileTools 工作空间隔离 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `FileTools` 与 `CommandLineTools`(bwarp)由同一份「纯允许列表」策略驱动:agent 默认只能访问自己的 workspace,其他一切不可读;额外只读根经配置显式添加。

**Architecture:** 新增 `FileAccessPolicy`(允许列表值对象)+ `FileAccessPolicyResolver`(从 workspace + 配置派生)。`FileTools` 用 .NET 路径校验(`IsReadable`/`IsWritable`)补齐缺失的读校验;bwarp 经新 `Sandbox.Confine(workingDirectory, command, readableRoots)` 重载改为 default-deny(精选系统路径只读 + workspace 可写 + 配置只读根),不再 `ro-bind /`。配置链:`StorageBuilder.AddReadableRoot` → `StorageSettings.FileIsolation.ReadableRoots` → `SettingsMerger` → `FileAccessPolicyResolver`。

**Tech Stack:** .NET 10, C# (record/primary ctors), xUnit + 手写 fake(无 mock 框架),bubblewrap(bwarp 库)。

**Spec:** `docs/superpowers/specs/2026-06-22-bwarp-workspace-isolation-design.md`

**已核实事实(实现时无需再查):**
- mount 类型均为 public positional record:`BindMount(string Source, string Destination, MountAccess Access = ReadWrite, bool Try = false)`、`TmpfsMount(string Destination, ...)`、`DirCreate(string Destination, ...)`、`ProcMount`、`DevMount`;基类 `MountEntry`。
- `SandboxOptions.Mounts` 为 `public IReadOnlyList<MountEntry>`;`SandboxBuilder.Build()` 返回 `SandboxOptions`;`SandboxBuilder` 构造为 `internal`,只能经 `Sandbox.Run/Confine` 公共工厂获得。
- `BwarpShellExecutor : IShellExecutor`、`ProcessShellExecutor : IShellExecutor`(DI 工厂按 `IShellExecutor` 返回,二者必须保持实现该接口)。
- 测试项目 `test/ManInBlack.AI.Tests` 引用 `ManInBlack.AI`(传递引用 Bwarp);`ManInBlack.AI` 对该测试项目有 `InternalsVisibleTo`。
- 既有 fake:`FakeUserWorkspace(string userId, string workingDir = "/tmp/workspace")`(`IUserWorkspace`);`Options.Create(value)` 构造 `IOptions<T>`。
- 既有 builder 测试用 `internal static ManInBlackSettings Merge(IServiceCollection services)` 断言合并结果。

---

## File Structure

- **Create** `src/ManInBlack.AI/Configuration/FileAccessPolicy.cs` — 允许列表值对象 + 路径辅助。
- **Create** `src/ManInBlack.AI/Configuration/FileAccessPolicyResolver.cs` — scoped 解析器。
- **Modify** `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs` — 新增 `FileIsolationSettings` + `StorageSettings.FileIsolation`。
- **Modify** `src/ManInBlack.AI/Configuration/SettingsMerger.cs` — 合并条件纳入 `FileIsolation`。
- **Modify** `src/ManInBlack.AI/Configuration/SubBuilders/StorageBuilder.cs` — 新增 `AddReadableRoot`。
- **Modify** `bwarp/Bwarp/SandboxBuilder.cs` — 新增 `ConfineBaseline()`。
- **Modify** `bwarp/Bwarp/Sandbox.cs` — `Confine` 改用 baseline + 带 `readableRoots` 重载。
- **Modify** `src/ManInBlack.AI/Services/BwarpShellExecutor.cs` — 注入 `FileAccessPolicy`,调用新 `Confine` 重载。
- **Modify** `src/ManInBlack.AI/Tools/FileTools.cs` — 注入 resolver,补齐读校验,Write/Edit 改 `IsWritable`。
- **Modify** `src/ManInBlack.AI/DependencyInjection.cs` — 注册 resolver + 工厂注入策略。
- **Create** `test/ManInBlack.AI.Tests/Configuration/FileAccessPolicyTests.cs`
- **Create** `test/ManInBlack.AI.Tests/Configuration/FileAccessPolicyResolverTests.cs`
- **Create** `test/ManInBlack.AI.Tests/Services/BwarpShellExecutorMountTests.cs`
- **Modify** `test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs` — 加 `AddReadableRoot` 合并测试。
- **Modify** `test/ManInBlack.AI.Tests/Tools/FileToolsTests.cs` — 适配 resolver ctor + 新增隔离读测试。

---

### Task 1: FileAccessPolicy 允许列表值对象

**Files:**
- Create: `src/ManInBlack.AI/Configuration/FileAccessPolicy.cs`
- Test: `test/ManInBlack.AI.Tests/Configuration/FileAccessPolicyTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/ManInBlack.AI.Tests/Configuration/FileAccessPolicyTests.cs`:

```csharp
using ManInBlack.AI.Configuration;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class FileAccessPolicyTests
{
    // workspace 与 temp 用不同根,避免互相包含造成误判
    private readonly string _ws = "/data/ws";
    private readonly string _tmp = "/data/tmp";

    private FileAccessPolicy Policy(params string[] roots) => new()
    {
        Workspace = _ws,
        Temp = _tmp,
        ReadableRoots = roots
    };

    [Fact]
    public void IsReadable_workspace内_true()
        => Assert.True(Policy().IsReadable("/data/ws/file.txt"));

    [Fact]
    public void IsReadable_workspace根本身_true()
        => Assert.True(Policy().IsReadable("/data/ws"));

    [Fact]
    public void IsReadable_temp内_true()
        => Assert.True(Policy().IsReadable("/data/tmp/scratch"));

    [Fact]
    public void IsReadable_配置只读根内_true()
        => Assert.True(Policy("/opt/data").IsReadable("/opt/data/x"));

    [Fact]
    public void IsReadable_列表外_false()
    {
        Assert.False(Policy().IsReadable("/root/.man-in-black/workspaces/other/secret"));
        Assert.False(Policy().IsReadable("/root/.man-in-black/settings.json"));
        Assert.False(Policy().IsReadable("/root/.man-in-black/sessions/abc"));
        Assert.False(Policy().IsReadable("/etc/passwd"));
    }

    [Fact]
    public void IsReadable_父目录穿越_false()
        => Assert.False(Policy().IsReadable("/data/ws/../tmp_evil/file"));

    [Fact]
    public void IsReadable_前缀伪匹配_false()
        => Assert.False(Policy().IsReadable("/data/ws-evil/file"));

    [Fact]
    public void IsWritable_workspace内_true()
        => Assert.True(Policy().IsWritable("/data/ws/a.txt"));

    [Fact]
    public void IsWritable_workspace根本身_false()
        => Assert.False(Policy().IsWritable("/data/ws"));

    [Fact]
    public void IsWritable_只读根内_false()
        => Assert.False(Policy("/opt/data").IsWritable("/opt/data/x"));

    [Fact]
    public void IsWritable_列表外_false()
        => Assert.False(Policy().IsWritable("/etc/passwd"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~FileAccessPolicyTests"`
Expected: FAIL — `FileAccessPolicy` 未定义(编译错误)。

- [ ] **Step 3: Write minimal implementation**

Create `src/ManInBlack.AI/Configuration/FileAccessPolicy.cs`:

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 文件访问「纯允许列表」策略:FileTools(.NET 校验)与 bwarp(挂载)的唯一共享真相。
/// 默认拒绝:仅 Workspace、Temp(可读写)与配置的 ReadableRoots(只读)可读。其余一律不可读。
/// 隔离强度 = 所配 ReadableRoots 的窄度;配 "/" 等于关闭隔离(操作者显式开关,见 spec §7)。
/// </summary>
public sealed record FileAccessPolicy
{
    /// <summary>可读写:当前用户 workspace(= IUserWorkspace.WorkingDirectory)。</summary>
    public string Workspace { get; init; } = "";

    /// <summary>可读写:系统临时目录。</summary>
    public string Temp { get; init; } = "";

    /// <summary>额外只读根(经配置添加)。默认空。</summary>
    public IReadOnlyList<string> ReadableRoots { get; init; } = [];

    public bool IsReadable(string resolvedPath) =>
        IsUnderOrEqual(resolvedPath, Workspace)
        || IsUnderOrEqual(resolvedPath, Temp)
        || ReadableRoots.Any(r => IsUnderOrEqual(resolvedPath, r));

    public bool IsWritable(string resolvedPath) =>
        IsUnder(resolvedPath, Workspace) || IsUnder(resolvedPath, Temp);

    /// <summary>规范化:取绝对路径并去掉尾部目录分隔符。</summary>
    internal static string Canonicalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>p 严格在 root 之下(以 "root/" 为前缀,且非仅前缀字符串相同)。</summary>
    internal static bool IsUnder(string path, string root)
    {
        var p = Canonicalize(path);
        var r = Canonicalize(root);
        if (r.Length == 0 || p.Length <= r.Length) return false;
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase)
            && (p[r.Length] == Path.DirectorySeparatorChar
                || p[r.Length] == Path.AltDirectorySeparatorChar);
    }

    /// <summary>p 等于 root 或在其下。</summary>
    internal static bool IsUnderOrEqual(string path, string root)
    {
        var p = Canonicalize(path);
        var r = Canonicalize(root);
        if (r.Length == 0) return false;
        return string.Equals(p, r, StringComparison.OrdinalIgnoreCase) || IsUnder(p, r);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~FileAccessPolicyTests"`
Expected: PASS(11 个用例)。

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/Configuration/FileAccessPolicy.cs test/ManInBlack.AI.Tests/Configuration/FileAccessPolicyTests.cs
git commit -m "✨ 新增 FileAccessPolicy 允许列表策略"
```

---

### Task 2: 配置接入(FileIsolationSettings + StorageSettings + 合并器 + AddReadableRoot)

> 一个任务内完成整条配置链,确保该 commit 编译且测试绿(避免中途不可编译)。

**Files:**
- Modify: `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs`
- Modify: `src/ManInBlack.AI/Configuration/SettingsMerger.cs`
- Modify: `src/ManInBlack.AI/Configuration/SubBuilders/StorageBuilder.cs`
- Test: `test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs`

- [ ] **Step 1: Write the failing test**

在 `test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs` 类内加:

```csharp
[Fact]
public void UseStorage_AddReadableRoot_合并保留()
{
    var services = new ServiceCollection();
    var builder = new ManInBlackBuilder(services);

    builder.UseStorage(s => s.RootPath("/data/mib")
        .AddReadableRoot("/opt/data")
        .AddReadableRoot("/srv/shared"));

    var settings = Merge(services);

    Assert.Equal(["/opt/data", "/srv/shared"],
        settings.Storage!.FileIsolation!.ReadableRoots);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~UseStorage_AddReadableRoot_合并保留"`
Expected: FAIL — `AddReadableRoot` / `FileIsolation` 未定义(编译错误)。

- [ ] **Step 3: Add FileIsolationSettings + StorageSettings.FileIsolation**

在 `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs` 的 `StorageSettings` 类内(`WorkspaceSettings? Workspace` 之后)加字段,并在文件末尾加新类型:

```csharp
public class StorageSettings
{
    public string? RootPath { get; set; }

    public WorkspaceSettings? Workspace { get; set; }

    /// <summary>文件隔离配置(额外只读根)。经 StorageBuilder.AddReadableRoot 写入。</summary>
    public FileIsolationSettings? FileIsolation { get; set; }
}

/// <summary>
/// 文件隔离配置:经配置显式追加的只读根。同时供 bwarp 挂载与 FileTools 校验。
/// </summary>
public class FileIsolationSettings
{
    /// <summary>额外只读根(系统运行时路径、MIB 指定路径等)。</summary>
    public List<string> ReadableRoots { get; set; } = [];
}
```

- [ ] **Step 4: Update SettingsMerger to preserve FileIsolation**

在 `src/ManInBlack.AI/Configuration/SettingsMerger.cs` 找到 Storage 合并条件(注释 `// Storage：仅当 source 有实质内容…`),把判断改为也认 `FileIsolation`:

```csharp
// Storage：仅当 source 有实质内容（非全默认）时覆盖
if (source.Storage is { } storage
    && (storage.RootPath is not null
        || storage.Workspace is not null
        || (storage.FileIsolation?.ReadableRoots.Count > 0)))
    target.Storage = storage;
```

- [ ] **Step 5: Add AddReadableRoot to StorageBuilder**

在 `src/ManInBlack.AI/Configuration/SubBuilders/StorageBuilder.cs` 的 `Workspace` 方法之后加:

```csharp
/// <summary>追加一个额外只读根(bwarp 与 FileTools 均据此放行)。</summary>
public StorageBuilder AddReadableRoot(string root)
{
    Settings.FileIsolation ??= new FileIsolationSettings();
    Settings.FileIsolation.ReadableRoots.Add(root);
    return this;
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~UseStorage_AddReadableRoot_合并保留"`
Expected: PASS。

- [ ] **Step 7: Commit**

```bash
git add src/ManInBlack.AI/Configuration/ManInBlackSettings.cs src/ManInBlack.AI/Configuration/SettingsMerger.cs src/ManInBlack.AI/Configuration/SubBuilders/StorageBuilder.cs test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs
git commit -m "✨ 新增 FileIsolation 配置链(ReadableRoots + 合并器 + AddReadableRoot)"
```

---

### Task 3: FileAccessPolicyResolver

**Files:**
- Create: `src/ManInBlack.AI/Configuration/FileAccessPolicyResolver.cs`
- Test: `test/ManInBlack.AI.Tests/Configuration/FileAccessPolicyResolverTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/ManInBlack.AI.Tests/Configuration/FileAccessPolicyResolverTests.cs`:

```csharp
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class FileAccessPolicyResolverTests
{
    [Fact]
    public void Resolve_无配置_仅workspace与temp可读()
    {
        var ws = "/data/ws/42";
        var resolver = new FileAccessPolicyResolver(
            new FakeUserWorkspace("42", ws),
            Options.Create(new ManInBlackSettings()));

        var policy = resolver.Resolve();

        Assert.Equal(ws.TrimEnd('/'), policy.Workspace);
        Assert.True(policy.IsReadable($"{ws}/file"));
        Assert.False(policy.IsReadable("/root/.man-in-black/workspaces/other/x"));
        Assert.Empty(policy.ReadableRoots);
    }

    [Fact]
    public void Resolve_有ReadableRoots_纳入只读根()
    {
        var settings = new ManInBlackSettings
        {
            Storage = new StorageSettings
            {
                FileIsolation = new FileIsolationSettings { ReadableRoots = ["/opt/data", "/srv/shared"] }
            }
        };
        var resolver = new FileAccessPolicyResolver(
            new FakeUserWorkspace("42", "/data/ws/42"),
            Options.Create(settings));

        var policy = resolver.Resolve();

        Assert.Equal(2, policy.ReadableRoots.Count);
        Assert.True(policy.IsReadable("/opt/data/sub/x"));
        Assert.False(policy.IsWritable("/opt/data/sub/x")); // 只读根不可写
    }

    [Fact]
    public void Resolve_只读根规范化()
    {
        var settings = new ManInBlackSettings
        {
            Storage = new StorageSettings
            {
                FileIsolation = new FileIsolationSettings { ReadableRoots = ["/opt/data/"] }
            }
        };
        var resolver = new FileAccessPolicyResolver(
            new FakeUserWorkspace("42", "/data/ws/42"),
            Options.Create(settings));

        var policy = resolver.Resolve();

        Assert.Equal("/opt/data", policy.ReadableRoots[0]); // 去尾分隔符
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~FileAccessPolicyResolverTests"`
Expected: FAIL — `FileAccessPolicyResolver` 未定义。

- [ ] **Step 3: Write minimal implementation**

Create `src/ManInBlack.AI/Configuration/FileAccessPolicyResolver.cs`:

```csharp
using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 从当前 workspace 与配置派生 <see cref="FileAccessPolicy"/>。
/// 不做位置派生:WorkingDirectory 是什么就是什么;ReadableRoots 全部来自配置。
/// </summary>
public sealed class FileAccessPolicyResolver(
    IUserWorkspace workspace,
    IOptions<ManInBlackSettings> settings)
{
    public FileAccessPolicy Resolve()
    {
        var roots = (settings.Value.Storage?.FileIsolation?.ReadableRoots ?? [])
            .Select(FileAccessPolicy.Canonicalize)
            .ToList();

        return new FileAccessPolicy
        {
            Workspace = FileAccessPolicy.Canonicalize(workspace.WorkingDirectory),
            Temp = FileAccessPolicy.Canonicalize(Path.GetTempPath()),
            ReadableRoots = roots
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~FileAccessPolicyResolverTests"`
Expected: PASS(3 个用例)。

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/Configuration/FileAccessPolicyResolver.cs test/ManInBlack.AI.Tests/Configuration/FileAccessPolicyResolverTests.cs
git commit -m "✨ 新增 FileAccessPolicyResolver"
```

---

### Task 4: bwarp ConfineBaseline + Confine(readableRoots) 重载

**Files:**
- Modify: `bwarp/Bwarp/SandboxBuilder.cs`(加 `ConfineBaseline`)
- Modify: `bwarp/Bwarp/Sandbox.cs`(`Confine` 改用 baseline,新增重载)
- Test: `test/ManInBlack.AI.Tests/Services/BwarpShellExecutorMountTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/ManInBlack.AI.Tests/Services/BwarpShellExecutorMountTests.cs`:

```csharp
using Bwarp;
using Bwarp.Mounts;
using Xunit;

namespace ManInBlack.AI.Tests.Services;

public class BwarpShellExecutorMountTests
{
    private static List<MountEntry> MountsOf(string ws, string command, string[]? roots = null) =>
        Sandbox.Confine(ws, command, roots ?? []).Build().Mounts.ToList();

    [Fact]
    public void 不绑定整个根目录()
    {
        var mounts = MountsOf("/data/ws/42", "ls");
        // 不允许 ro-bind "/ /"(那会泄露同级 workspace 与密钥)
        Assert.DoesNotContain(mounts, m => m is BindMount b && b.Source == "/");
        Assert.DoesNotContain(mounts, m => m is BindMount b && b.Destination == "/");
    }

    [Fact]
    public void 含精选系统只读路径()
    {
        var mounts = MountsOf("/data/ws/42", "ls");
        Assert.Contains(mounts, m => m is BindMount b => b.Destination == "/usr" && b.Access == MountAccess.ReadOnly);
        Assert.Contains(mounts, m => m is BindMount b => b.Destination == "/etc" && b.Access == MountAccess.ReadOnly);
        Assert.Contains(mounts, m => m is ProcMount);
        Assert.Contains(mounts, m => m is DevMount);
        Assert.Contains(mounts, m => m is TmpfsMount t => t.Destination == "/tmp");
    }

    [Fact]
    public void workspace可写绑定_且在系统路径之后()
    {
        var mounts = MountsOf("/data/ws/42", "ls");
        var wsIdx = mounts.FindIndex(m => m is BindMount b
            && b.Source == "/data/ws/42" && b.Destination == "/data/ws/42" && b.Access == MountAccess.ReadWrite);
        var usrIdx = mounts.FindIndex(m => m is BindMount b => b.Destination == "/usr");
        Assert.True(wsIdx >= 0, "缺少 workspace 可写绑定");
        Assert.True(usrIdx >= 0 && usrIdx < wsIdx, "workspace 可写绑定必须在系统路径之后");
    }

    [Fact]
    public void 只读根被只读绑定()
    {
        var mounts = MountsOf("/data/ws/42", "ls", ["/opt/data"]);
        Assert.Contains(mounts, m => m is BindMount b
            && b.Source == "/opt/data" && b.Destination == "/opt/data" && b.Access == MountAccess.ReadOnly);
    }

    [Fact]
    public void 同级workspace路径未被挂载()
    {
        var mounts = MountsOf("/data/ws/42", "ls");
        Assert.DoesNotContain(mounts, m => m is BindMount b
            && b.Destination.StartsWith("/data/ws/", StringComparison.Ordinal)
            && b.Destination != "/data/ws/42");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~BwarpShellExecutorMountTests"`
Expected: FAIL — `Sandbox.Confine(ws, cmd, roots)` 三参重载未定义。

- [ ] **Step 3: Add ConfineBaseline to SandboxBuilder**

在 `bwarp/Bwarp/SandboxBuilder.cs`(filesystem mounts 区段,`RemountReadOnly` 之后)加:

```csharp
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
```

- [ ] **Step 4: Refactor Sandbox.Confine to use ConfineBaseline + add readableRoots overload**

把 `bwarp/Bwarp/Sandbox.cs` 的 `Confine` 方法替换为:

```csharp
/// <summary>
/// Confine a shell command to a working directory (writable), in a default-deny sandbox.
/// 只读暴露精选系统路径(供命令运行)与 <paramref name="readableRoots"/>;不绑定整个宿主 FS,
/// 故同级 workspace 与宿主其他用户数据不可见。Network access is allowed.
/// </summary>
public static SandboxBuilder Confine(string workingDirectory, string command, IReadOnlyList<string>? readableRoots = null)
{
    var home = Environment.GetEnvironmentVariable("HOME") ?? "/root";
    var sb = new SandboxBuilder()
        .WithCommand("/bin/bash", "-c", command)
        .ConfineBaseline()
        .CreateDir(workingDirectory)
        .Bind(workingDirectory, workingDirectory);

    if (readableRoots is not null)
    {
        foreach (var root in readableRoots)
            sb.CreateDir(root).BindReadOnly(root, root);
    }

    return sb
        .TryBind($"{home}/.cache", $"{home}/.cache")
        .WithWorkingDirectory(workingDirectory);
}

/// <summary>旧入口,等价于 readableRoots 为 null。</summary>
public static SandboxBuilder Confine(string workingDirectory, string command)
    => Confine(workingDirectory, command, readableRoots: null);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~BwarpShellExecutorMountTests"`
Expected: PASS(5 个用例)。

- [ ] **Step 6: Commit**

```bash
git add bwarp/Bwarp/SandboxBuilder.cs bwarp/Bwarp/Sandbox.cs test/ManInBlack.AI.Tests/Services/BwarpShellExecutorMountTests.cs
git commit -m "♻️ bwarp 改用 default-deny 精选路径,新增 readableRoots 重载"
```

---

### Task 5: BwarpShellExecutor 注入策略

**Files:**
- Modify: `src/ManInBlack.AI/Services/BwarpShellExecutor.cs`

- [ ] **Step 1: Update BwarpShellExecutor to inject FileAccessPolicy and use the new overload**

把 `src/ManInBlack.AI/Services/BwarpShellExecutor.cs` 整体替换为(保留 `: IShellExecutor`):

```csharp
using Bwarp;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;

namespace ManInBlack.AI.Services;

/// <summary>
/// 基于 Bwarp (bubblewrap) 沙盒的 Shell 执行器,用于 Linux。
/// 隔离由 FileAccessPolicy 驱动:只暴露 workspace(可写)+ 配置只读根 + 精选系统路径。
/// </summary>
public class BwarpShellExecutor(FileAccessPolicy policy) : IShellExecutor
{
    public ShellResult Execute(string command, string workingDirectory, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            var result = Sandbox.Confine(policy.Workspace, command, policy.ReadableRoots)
                .ExecuteAsync(cts.Token)
                .GetAwaiter()
                .GetResult();

            return new ShellResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
            };
        }
        catch (OperationCanceledException)
        {
            return new ShellResult { ExitCode = -1, TimedOut = true };
        }
    }
}
```

> `Execute` 仍实现 `IShellExecutor`,保留 `workingDirectory` 参数仅为匹配接口签名;实际工作目录以 `policy.Workspace`(由 resolver 从 `IUserWorkspace.WorkingDirectory` 派生,与调用方传入值一致)为准。Task 7 工厂注入解析后的 policy。

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ManInBlack.AI/ManInBlack.AI.csproj`
Expected: BUILD succeeded。(DI 工厂在 Task 7 才改为传入 policy;此步只验证类型正确。)

- [ ] **Step 3: Commit**

```bash
git add src/ManInBlack.AI/Services/BwarpShellExecutor.cs
git commit -m "♻️ BwarpShellExecutor 注入 FileAccessPolicy"
```

---

### Task 6: FileTools 接入策略(补齐读校验)

**Files:**
- Modify: `src/ManInBlack.AI/Tools/FileTools.cs`
- Modify: `test/ManInBlack.AI.Tests/Tools/FileToolsTests.cs`

- [ ] **Step 1: Write the failing tests**

在 `test/ManInBlack.AI.Tests/Tools/FileToolsTests.cs`:
(a) using 区加 `using ManInBlack.AI.Configuration;` 与 `using Microsoft.Extensions.Options;`;
(b) 构造函数里把 `var workspace = new FakeUserWorkspace(...)` 之后的 `_tools = new FileTools(workspace);` 改为经 resolver 构造:

```csharp
public FileToolsTests()
{
    _workspaceDir = Path.Combine(Path.GetTempPath(), $"mib_test_ws_{Guid.NewGuid():N}");
    _tempDir = Path.Combine(Path.GetTempPath(), $"mib_test_tmp_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_workspaceDir);
    Directory.CreateDirectory(_tempDir);

    var workspace = new FakeUserWorkspace("test-user", _workspaceDir);
    var resolver = new FileAccessPolicyResolver(workspace, Options.Create(new ManInBlackSettings()));
    _tools = new FileTools(resolver);
}
```

(c) 类内(保留原有 Write/Edit 测试)加:

```csharp
#region Read 隔离测试

[Fact]
public async Task Read_workspace内_成功()
{
    var filePath = Path.Combine(_workspaceDir, "readable.txt");
    File.WriteAllText(filePath, "hello");

    var content = await _tools.Read(filePath);

    Assert.Equal("hello", content);
}

[Fact]
public async Task Read_临时目录内_成功()
{
    var filePath = Path.Combine(_tempDir, "tmp.txt");
    File.WriteAllText(filePath, "tmpdata");

    var content = await _tools.Read(filePath);

    Assert.Equal("tmpdata", content);
}

[Fact]
public async Task Read_允许列表外_拒绝()
{
    var outside = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "outside.txt");
    File.WriteAllText(outside, "secret");
    try
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _tools.Read(outside));
    }
    finally
    {
        if (File.Exists(outside)) File.Delete(outside);
    }
}

[Fact]
public void Glob_允许列表外的根_拒绝()
{
    var outsideDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "glob_outside_dir");
    Directory.CreateDirectory(outsideDir);
    try
    {
        Assert.Throws<UnauthorizedAccessException>(() => _tools.Glob("*.txt", outsideDir));
    }
    finally
    {
        if (Directory.Exists(outsideDir)) Directory.Delete(outsideDir, true);
    }
}

[Fact]
public void Glob_workspace内_返回结果()
{
    var inside = Path.Combine(_workspaceDir, "a.txt");
    File.WriteAllText(inside, "x");

    var result = _tools.Glob("*.txt", _workspaceDir);

    Assert.Contains(inside, result);
}

#endregion
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~FileToolsTests"`
Expected: FAIL — `FileTools` ctor 不接受 `FileAccessPolicyResolver`(编译错误)。

- [ ] **Step 3: Update FileTools to inject resolver and apply policy**

在 `src/ManInBlack.AI/Tools/FileTools.cs`:

(a) using 区加 `using ManInBlack.AI.Configuration;`;

(b) 把类声明与字段改为(删掉原 `_userWorkspace`、`_tempDirectory` 字段):

```csharp
[ServiceRegister.Scoped]
public partial class FileTools(FileAccessPolicyResolver resolver)
{
    private readonly FileAccessPolicy _policy = resolver.Resolve();
```

(c) `ResolvePath` 改用 `_policy.Workspace`:

```csharp
private string ResolvePath(string path) =>
    Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(_policy.Workspace, path));
```

(d) `Read` 在 `filePath = ResolvePath(filePath);` 之后、`File.Exists` 之前加校验:

```csharp
filePath = ResolvePath(filePath);
if (!_policy.IsReadable(filePath))
    throw new UnauthorizedAccessException($"{OutOfAllowedDirectoryError} Path: {filePath}");
if (!File.Exists(filePath))
    throw new FileNotFoundException($"文件不存在: {filePath}", filePath);
```

(e) `Write` 与 `Edit` 里 `if (!IsInsideAllowedDirectory(filePath))` 改为 `if (!_policy.IsWritable(filePath))`。

(f) `Glob` 与 `Grep` 改为(根校验 + 用 `_policy.Workspace` 默认根):

```csharp
public string Glob(string pattern, string? directory = null)
{
    var searchDir = directory is null ? _policy.Workspace : ResolvePath(directory);
    if (!_policy.IsReadable(searchDir))
        throw new UnauthorizedAccessException($"{OutOfAllowedDirectoryError} Path: {searchDir}");
    if (!Directory.Exists(searchDir))
        throw new DirectoryNotFoundException($"目录不存在: {searchDir}");

    var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
    matcher.AddInclude(pattern);
    var matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(searchDir)));
    var sorted = matchResult.Files
        .Select(f => Path.GetFullPath(Path.Combine(searchDir, f.Path)))
        .Select(f => new FileInfo(f))
        .OrderByDescending(f => f.LastWriteTimeUtc)
        .Select(f => f.FullName);

    var result = string.Join(Environment.NewLine, sorted);
    return result.Length == 0 ? "No files matched the pattern." : result;
}

public string Grep(string pattern, string? directory = null, string glob = "*")
{
    var searchDir = directory is null ? _policy.Workspace : ResolvePath(directory);
    if (!_policy.IsReadable(searchDir))
        throw new UnauthorizedAccessException($"{OutOfAllowedDirectoryError} Path: {searchDir}");
    if (!Directory.Exists(searchDir))
        throw new DirectoryNotFoundException($"目录不存在: {searchDir}");

    var regex = new Regex(pattern, RegexOptions.Compiled);
    var files = Directory.EnumerateFiles(searchDir, glob, SearchOption.AllDirectories);

    var results = new List<string>();
    foreach (var file in files)
    {
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length; i++)
        {
            if (regex.IsMatch(lines[i]))
                results.Add($"{file}:{i + 1}: {lines[i]}");
        }
    }

    return results.Count == 0
        ? "No matches found."
        : string.Join(Environment.NewLine, results);
}
```

(g) 删除 `IsInsideWorkspace`、`IsInsideTempDirectory`、`IsInsideAllowedDirectory`、`IsExactPath` 四个私有方法(已由 `_policy` 取代);`OutOfAllowedDirectoryError` 常量保留。

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~FileToolsTests"`
Expected: PASS(原有 Write/Edit + 新增 Read/Glob 隔离用例)。

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/Tools/FileTools.cs test/ManInBlack.AI.Tests/Tools/FileToolsTests.cs
git commit -m "♻️ FileTools 接入 FileAccessPolicy,补齐读校验"
```

---

### Task 7: DI 接线 + 全量验证

**Files:**
- Modify: `src/ManInBlack.AI/DependencyInjection.cs`

- [ ] **Step 1: Register resolver and wire BwarpShellExecutor factory**

在 `src/ManInBlack.AI/DependencyInjection.cs` 的 `AddManInBlack()` extension 内:

(a) 在 `services.AddScoped<IUserWorkspace>(...)` 之后注册 resolver:

```csharp
services.AddScoped<FileAccessPolicyResolver>();
```

(b) 把 `IShellExecutor` 工厂改为向 `BwarpShellExecutor` 注入解析后的 policy:

```csharp
services.AddScoped<IShellExecutor>(sp =>
{
    var useSandbox = sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value.UseSandbox;
    if (OperatingSystem.IsLinux() && useSandbox)
    {
        var policy = sp.GetRequiredService<FileAccessPolicyResolver>().Resolve();
        return new BwarpShellExecutor(policy);
    }
    return new ProcessShellExecutor();
});
```

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build ManInBlack.slnx`
Expected: BUILD succeeded,0 errors。

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test ManInBlack.slnx`
Expected: 全部 PASS(含新 FileAccessPolicy / Resolver / bwarp mount / FileTools 隔离用例,及既有用例不回归)。

- [ ] **Step 4: Manual verification on Linux(操作者执行,非 CI)**

在目标 Linux 机器上以 `UseSandbox=true` 运行,确认:
- agent 在自己 workspace 内可读写;
- `RunBash` 执行 `cat /root/.man-in-black/workspaces/<其他用户>/...` 失败(路径不存在);
- `RunBash` 执行 `ls /root/.man-in-black/workspaces/` 看不到其他用户目录;
- 配了 `AddReadableRoot("/opt/data")` 后,`Read` 与 `RunBash` 均可读 `/opt/data` 下文件。

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/DependencyInjection.cs
git commit -m "🔌 DI 注册 FileAccessPolicyResolver 并注入 BwarpShellExecutor"
```

---

## Self-Review(写计划后自查,已并入正文)

- **Spec coverage**:§1 FileAccessPolicy → Task 1;§2 Resolver → Task 3;§3 bwarp mount plan → Task 4+5;§4 FileTools → Task 6;§5 配置 → Task 2;§6 DI → Task 7;§7 边界(部分隔离态等)→ Task 7 Step 4 手动验证;§8 测试 → 各 Task 内 TDD。全覆盖。
- **Placeholder scan**:无 TBD/TODO;每步含完整代码或确切命令。
- **Type consistency**:`FileAccessPolicy`(Workspace/Temp/ReadableRoots/IsReadable/IsWritable)、`FileAccessPolicyResolver(IUserWorkspace, IOptions<ManInBlackSettings>)`、`StorageSettings.FileIsolation`、`FileIsolationSettings.ReadableRoots`、`StorageBuilder.AddReadableRoot`、`Sandbox.Confine(ws, cmd, IReadOnlyList<string>?)`、`BwarpShellExecutor(FileAccessPolicy) : IShellExecutor`、`FileTools(FileAccessPolicyResolver)` —— 跨任务命名一致。
- **编译连续性**:每个 Task 的 commit 都能 `dotnet build` 通过(Task 2 把配置类型 + builder 方法放在同一 commit,避免中途不可编译)。
