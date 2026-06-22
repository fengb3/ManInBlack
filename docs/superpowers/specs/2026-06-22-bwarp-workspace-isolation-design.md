# bwarp + FileTools 工作空间隔离

## 背景

`/root/.man-in-black/workspaces/` 下按用户存放多个工作空间目录(`UserIsolated` 模式解析为 `{RootPath}/workspaces/{userId}`)。要求:**任一用户的 agent 默认只能访问自己的 workspace,其他一切(同级 workspace、`settings.json`、`sessions/`、宿主其他文件)默认不可读**;需要读的其他路径(系统运行时路径、MIB 指定路径)经配置**显式**追加。且 `CommandLineTools` 与 `FileTools` 两条路径必须同时满足。

两条路径今日的缺口:

- **`CommandLineTools`** 经 `BwarpShellExecutor` → `Sandbox.Confine`。`Confine` 做 `BindReadOnly("/", "/")`,把整个宿主文件系统(含所有同级 workspace 与 `settings.json`)只读暴露。
- **`FileTools`** 不走 bwarp,直接用 .NET `File`/`Directory` API。`Write`/`Edit` 有 `IsInsideAllowedDirectory` 校验,但 **`Read`(FileTools.cs:117)完全没有路径校验**,`Glob`/`Grep` 接受绝对 `directory`——任意文件可读。

## 目标

- **默认拒绝(default-deny 允许列表)**:agent 默认只能读写自己的 workspace;其他一切默认不可读、不可见。同级 workspace 隔离是「不在允许列表里」的自然结果,无需特判。
- **显式追加只读根**:需要读的系统运行时路径 / MIB 指定路径,经配置加入只读根列表。
- **双路径一致**:`CommandLineTools`(bwarp)与 `FileTools`(.NET)由同一份策略驱动。
- **与 workspace 位置无关**:不根据「workspace 是否在 `workspaces/` 下」做特判;workspace 就是 `IUserWorkspace.WorkingDirectory` 解析出的那个目录,规则统一。

## 非目标(本期不做)

- 不做按用户的细粒度 ACL / 权限矩阵。
- 不自动暴露 RootPath、不自动 deny `settings.json`/`sessions/`(改为:默认什么都不额外暴露,需要就显式配只读根;密钥/隐私因不在允许列表而天然不可读)。
- 不把 `FileTools` 改成走 shell 实现(已评估否决,见 §7 备选记录)。
- 不做「移除默认项」的反向配置(默认只有 workspace,无需移除)。
- 不覆盖 `Write`/`Edit` 之外的破坏性操作(`Delete*` 仍注释关闭)。

## 关键决策

1. **纯允许列表策略**:`IsReadable`/`IsWritable` 默认只覆盖用户自己的 workspace;额外只读根经配置添加。无 deny-list、无 `workspaces/` 位置派生、无 `WorkspacesContainer` 挖空。
2. **隔离始终启用、与 workspace 位置无关、与 `UseSandbox` 无关(FileTools 侧)**。`CurrentDirectory` / `CustomPath` 模式同样适用(行为收紧,见 §7)。
3. **bwarp 从 `ro-bind /` 改为「精选系统路径只读 + workspace 可写 + 配置的额外只读根」**,实现 default-deny。`CommandLineTools` 不再能读同级 workspace 与密钥。
4. **FileTools 走 .NET 路径校验**(共享策略),非 shell。

## 设计

### 1. 策略模型 `FileAccessPolicy`(新建,`ManInBlack.AI.Configuration`)

不可变值对象,两条路径的**唯一共享真相**。纯允许列表,无 deny、无位置字段。

```csharp
public sealed record FileAccessPolicy
{
    /// <summary>可读写:当前用户 workspace(= IUserWorkspace.WorkingDirectory)。</summary>
    public string Workspace { get; init; } = "";
    /// <summary>可读写:系统临时目录(系统暂存区,默认开启;如需更严可经配置移出)。</summary>
    public string Temp { get; init; } = "";
    /// <summary>额外只读根(经配置添加:系统运行时路径、MIB 指定路径等)。默认空。</summary>
    public IReadOnlyList<string> ReadableRoots { get; init; } = [];

    public bool IsReadable(string resolvedPath) =>
        IsUnderOrEqual(resolvedPath, Workspace)
        || IsUnderOrEqual(resolvedPath, Temp)
        || ReadableRoots.Any(r => IsUnderOrEqual(resolvedPath, r));

    public bool IsWritable(string resolvedPath) =>
        (IsUnder(resolvedPath, Workspace) || IsUnder(resolvedPath, Temp));  // 严格在内,保留「禁操作根」保护
}
```

**路径辅助**(`OrdinalIgnoreCase`,沿用现有 `IsInsideWorkspace` 写法):
- `Canonicalize(p)` = `Path.GetFullPath(p).TrimEnd(分隔符)`,空安全。
- `IsUnder(p, root)` = `p` 严格在 `root` 之下(`root/` 前缀)。
- `IsUnderOrEqual(p, root)` = 等于 `root` 或在其下。

**语义**:
- `IsReadable` = 在 workspace 内 / 在 temp 内 / 在任一配置只读根内。其余一律 **false**(同级 workspace、settings.json、sessions/、宿主其他文件均不在列表 → 不可读)。
- `IsWritable` = 严格在 workspace 或 temp 内(非根本身,保留现有根保护)。其余 **false**。

> 不需要 `Isolated`/`WorkspacesContainer`/`DeniedSubtrees`:默认拒绝本身就是隔离。同级 workspace 与密钥因不在允许列表而天然不可读——比「广泛暴露 + 定点掩盖」更简单也更安全。

### 2. 策略解析 `FileAccessPolicyResolver`(新建,scoped)

```csharp
public sealed class FileAccessPolicyResolver(
    IOptions<AgentStorageOptions> options,
    IUserWorkspace workspace,
    IOptions<ManInBlackSettings> settings)
{
    public FileAccessPolicy Resolve()
    {
        var ws   = Canonicalize(workspace.WorkingDirectory);
        var temp = Canonicalize(Path.GetTempPath());
        var roots = (settings.Value.FileIsolation?.ReadableRoots ?? [])
                    .Select(Canonicalize).ToList();
        return new FileAccessPolicy { Workspace = ws, Temp = temp, ReadableRoots = roots };
    }
}
```

无位置派生:`WorkingDirectory` 是什么就是什么,规则统一。

### 3. bwarp mount plan(改 `BwarpShellExecutor` + bwarp 库抽 baseline)

**核心变化:不再 `BindReadOnly("/", "/")`** —— 否则 `CommandLineTools` 仍能读同级 workspace 与密钥,与「双路径一致」相悖。改为 default-deny 的精选挂载。

**bwarp 库侧**(`SandboxBuilder` 增实例方法,保持工具箱定位):

```csharp
/// <summary>default-deny 基线:只读绑定精选系统路径(供命令运行所需)+ proc/dev/tmp。
/// 不绑定 / ,不绑定用户数据;调用方随后绑定 workspace 与额外只读根。</summary>
public SandboxBuilder ConfineBaseline() { ... }
```

精选系统只读路径(可配置,默认值覆盖常见 Linux):
`/usr`、`/lib`、`/lib64`、`/bin`、`/sbin`、`/etc`、`/run`(try)、`/opt`(try);加 `MountProc`、`MountDev`、`MountTmpfs("/tmp")`。必要时 `CreateDir` workspace 祖先链(供 bind 目标存在),`HOME` 处理(`CreateDir` 空 `/root` 或将 `HOME` 指向 workspace/tmp)。

**`Sandbox.Confine`** 内部改为:`ConfineBaseline()` + 绑定 workspace + `.TryBind(home/.cache, ...)` + `DieWithParent`/`NewSession`/`WithWorkingDirectory`。

**`BwarpShellExecutor` 侧**:注入 `FileAccessPolicy`,调用 `Sandbox.Confine(workingDirectory, command, policy.ReadableRoots)`。**可写目录取调用方传入的 `workingDirectory`**(`IShellExecutor` 契约:`CommandLineTools` 传用户 workspace,`HookExecutor` 全局钩子传 `{RootPath}/hooks/`),`policy` 仅提供 `ReadableRoots`;系统路径由 baseline 默认挂载:

```
# Sandbox.Confine(workingDirectory, command, readableRoots) 内部等价于:
var sb = new SandboxBuilder()
    .WithCommand("/bin/bash", "-c", command)
    .ConfineBaseline();                              # 精选系统路径 ro + proc/dev/tmp(不绑定 /)
# —— 调用方 workingDirectory 可写(= workspace 或 hooks/)——
CreateDir(workingDirectory 祖先链); sb.Bind(workingDirectory, workingDirectory)
# —— 配置的额外只读根 ——
foreach r in policy.ReadableRoots:
    CreateDir(r 祖先链); sb.BindReadOnly(r, r)
sb.TryBind(home/.cache, home/.cache)
  .DieWithParent().NewSession().WithWorkingDirectory(workingDirectory)
```

> 不用 `policy.Workspace` 作可写目录:`IShellExecutor` 被两类调用方共用,全局钩子的工作目录是 `{RootPath}/hooks/` 而非用户 workspace;若强行用 `policy.Workspace`,全局钩子的脚本目录与 CWD 会丢失。

效果:沙盒内只有「精选系统路径(只读,供命令运行)+ 调用方 workingDirectory(可写)+ 配置只读根(只读)」。同级 workspace、`settings.json`、`sessions/` **根本未被挂载**,不可见。命令仍可运行(有 `/usr` 等)。

> 精选系统路径列表是 default-deny 的代价:若某命令需要未列入的路径(如 `/nix`、`/var/cache`),经配置把该路径加入只读根,或扩充默认列表。默认列表覆盖主流场景。

### 4. FileTools 改造(改 `FileTools.cs`)

ctor 增参 `FileAccessPolicyResolver`,`Resolve()` 一次(FileTools scoped)。用策略替换现有 `IsInsideWorkspace`/`IsInsideTempDirectory`/`IsInsideAllowedDirectory`:

- **`Read`**:解析路径后新增 `if (!policy.IsReadable(p)) throw new UnauthorizedAccessException(...)`——**补上当前缺失的读校验**。
- **`Glob`/`Grep`**:解析 `searchDir` 后先 `IsReadable(searchDir)` 校验根;再对每条结果路径过 `IsReadable` 过滤。
- **`Write`/`Edit`**:`IsInsideAllowedDirectory(p)` → `policy.IsWritable(p)`,根保护语义不变。

现有 `_userWorkspace`/`_tempDirectory` 字段改读 `policy.Workspace`/`policy.Temp`;`OutOfAllowedDirectoryError` 文案保留。

### 5. 配置接入(扩展新 builder)

`ManInBlackSettings` 增 `FileIsolation` 节:

```csharp
public class FileIsolationSettings
{
    /// <summary>额外只读根(系统运行时路径、MIB 指定路径等)。同时供 bwarp 挂载与 FileTools 校验。</summary>
    public List<string> ReadableRoots { get; set; } = [];
}
// ManInBlackSettings 增: public FileIsolationSettings? FileIsolation { get; set; }
```

`StorageBuilder` 增 fluent(沿用既有 `SettingsMerger`/`IManInBlackContribution` 合并流):

```csharp
public StorageBuilder AddReadableRoot(string root) { ... }
```

无 deny 配置(YAGNI;需要更细粒度时再追加)。默认只有 workspace,无需移除任何项。

### 6. DI 接线(改 `DependencyInjection.cs`)

- 注册 `FileAccessPolicyResolver`(scoped)。
- `FileTools` ctor 增参 `FileAccessPolicyResolver`(`[ServiceRegister.Scoped]` 自动注册)。
- `BwarpShellExecutor` ctor 增参 `FileAccessPolicy`;`AddScoped<IShellExecutor>` 工厂改为 `new BwarpShellExecutor(sp.GetRequiredService<FileAccessPolicyResolver>().Resolve())`。

### 7. 边界与已知 trade-off

- **`ReadableRoots` 是操作者显式开关(故意不设护栏)**:配置宽根会相应放宽隔离——`AddReadableRoot("/")` = 两条路径全局可读(隔离等同关闭);`/root` 等敏感数据祖先根会泄露同级 workspace / `settings.json` / `sessions/`。这是赋予部署方最大自由的显式设计选择(`ReadableRoots` 仅操作者可配,非 agent 可控),不加校验拦截。操作者负责只配窄根;隔离强度 = 所配根的窄度。
- **行为收紧(全模式)**:允许列表始终启用,`CurrentDirectory` / `CustomPath` 模式的 `Read`/`Glob`/`Grep` 也被限定在 workspace + 配置只读根内(今日为任意可读)。这是「默认只有 workspace」的必然结果;若某些场景需放宽,经配置加只读根。
- **bwarp 精选路径风险**:`ConfineBaseline` 的系统路径列表若遗漏某命令所需路径,该命令失败;经配置加只读根或扩充默认列表解决。
- **部分隔离态**:`UseSandbox=false` 时 `FileTools` 受限(经 .NET 校验),但 `CommandLineTools` 走 `ProcessShellExecutor` **不受限**(bwarp 是它唯一隔离手段)。要双工具全隔离须开 `UseSandbox`。
- **全局钩子 + UseSandbox(范围外,既有问题)**:`IShellExecutor` 被 `CommandLineTools` 与 `HookExecutor` 共用;`BwarpShellExecutor` 故意以调用方 `workingDirectory`(而非 `policy.Workspace`)为可写目录,使全局钩子的 `{RootPath}/hooks/` 脚本目录与 CWD 可用。但 `HookExecutor` 另把上下文写入宿主 `/tmp` 临时文件再传给脚本,而沙盒挂载的是私有 `/tmp` tmpfs——故该组合下临时文件对脚本不可见。此问题在旧 `ro-bind /` 下同样存在,本期不修;如需支持,后续让钩子经 stdin/env 传上下文,或把宿主 `/tmp` 以可写 bind 挂入沙盒。
- **Windows / 非 Linux**:bwarp 不跑(`OperatingSystem.IsLinux()` 门);`FileTools` 路径校验照常生效。
- **路径比较**:沿用现有 `OrdinalIgnoreCase`(Linux 实际路径全小写,无实际影响),记为既有约束。
- **`Temp` 默认可写**:作为系统暂存区默认开启;若要更严(仅 workspace 可写),后续可经配置移出,本期不动。
- **备选记录**:曾评估「`FileTools` 走 shell 实现以复用 bwarp 单一执行点」,否决原因:① 与「隔离始终启用」决策冲突(shell 仅 `UseSandbox` 时隔离);② 命令注入面;③ 语义损耗(`Edit` 精确替换、二进制探测、UTF-8/中文 locale、异常映射);④ 进程开销;⑤ 推翻 `RunBash` docstring「优先专用工具而非 cat/sed/grep」的设计意图。纯允许列表方案下 FileTools 隔离经 .NET 路径校验实现,语义精确、平台无关。

### 8. 测试(`test/ManInBlack.AI.Tests`,xUnit + 手写 fake,沿用本仓约定)

不跑真实 bwrap(CI 跨平台);只验参数构造与 .NET 语义。

| 用例 | 验证点 |
|------|--------|
| `IsReadable` — workspace/temp 内 | true |
| `IsReadable` — 配置只读根内 | true |
| `IsReadable` — 同级 workspace / `settings.json` / `sessions/` / 宿主其他 | false(不在列表) |
| `IsReadable` — `../` 穿透到列表外 | false |
| `IsReadable` — 空配置(仅 workspace) | 仅 workspace/temp 可读 |
| `IsWritable` — workspace 内 / 根本身 / 只读根内 / 外部 | true / false / false / false |
| `Resolver` — `ReadableRoots` 规范化与合并 | 正确 |
| `BwarpShellExecutor` mount 序列 | **无 `ro-bind / /`**;含精选系统路径 ro;workspace `bind` 存在;配置只读根 `ro-bind` 存在;无同级 workspace 路径 |
| `FileTools.Read` | workspace 可读;同级/密钥不可读 |
| `FileTools.Glob` | 结果仅含可读根内;列表外被过滤 |
| `FileTools.Write/Edit` | 维持 workspace/temp 约束 |

mount 序列测试:把 `BwarpShellExecutor` 的沙盒构造拆成可测入口——`internal SandboxOptions BuildSandboxOptions(FileAccessPolicy policy, string command)`,`Execute` 调用它再 `ExecuteAsync`。测试对其 `Mounts` 断言类型与顺序,不调真实 bwrap。

## 涉及文件

- 新增 `src/ManInBlack.AI/Configuration/FileAccessPolicy.cs`
- 新增 `src/ManInBlack.AI/Configuration/FileAccessPolicyResolver.cs`
- 新增 `src/ManInBlack.AI/Configuration/FileIsolationSettings.cs`(或并入 `ManInBlackSettings.cs`)
- 改 `bwarp/Bwarp/SandboxBuilder.cs`(增 `ConfineBaseline`,default-deny 系统路径)
- 改 `bwarp/Bwarp/Sandbox.cs`(`Confine` 改用 `ConfineBaseline`,去掉 `ro-bind / /`)
- 改 `src/ManInBlack.AI/Services/BwarpShellExecutor.cs`(注入策略 + 组装 mount plan)
- 改 `src/ManInBlack.AI/Tools/FileTools.cs`(注入 resolver + 读路径校验 + Write/Edit 改用策略)
- 改 `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs`(增 `FileIsolation`)
- 改 `src/ManInBlack.AI/Configuration/SubBuilders/StorageBuilder.cs`(增 `AddReadableRoot`)
- 改 `src/ManInBlack.AI/DependencyInjection.cs`(注册 resolver + 工厂注入策略)
- 新增 `test/ManInBlack.AI.Tests/`(FileAccessPolicy / Resolver / BwarpShellExecutor mount 序列 / FileTools 测试)
