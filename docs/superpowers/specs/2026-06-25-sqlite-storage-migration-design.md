# SQLite 存储迁移设计

- 日期：2026-06-25
- 状态：待审阅
- 范围：将 ManInBlack 后台持久化的运行期数据从 JSON 文件迁移到 SQLite

## 1. 背景与目标

当前后台数据以 JSON 文件落到 `AgentStorageOptions.RootPath`（默认 `~/.man-in-black`）下：

| # | 数据 | 当前落盘方式 | 位置 |
|---|------|------------|------|
| 1 | 对话历史（`ChatMessage`） | JSONL 追加 | `{RootPath}/sessions/{sessionId}.jsonl` |
| 2 | Agent 状态快照（`AgentStateSnapshot`） | JSON（原子替换） | `{RootPath}/sessions/{sessionId}.state.json` |
| 3 | 用户 ID 映射（`Dictionary<原始id, 数字id>`） | JSON 字典 | `{RootPath}/users/userIdMap.json` |
| 4 | 用户条目（`UserEntry`） | JSON | `{RootPath}/users/{数字id}.json` |
| 5 | 系统配置 | JSON | `~/.man-in-black/settings.json` |
| 6 | Agent profile | Markdown | `{RootPath}/profile.md` |
| 7 | 用户 hook 配置 | JSON | `{workspace}/.agents/mib-hooks.json` |

**目标**：把运行期数据（1–4）迁到单个 SQLite 文件。配置类文件（5–7）保持文件系统存储——它们更适合人类阅读/编辑、被 `IConfiguration` 加载、可版本控制。

项目已有干净的存储抽象层（`ISessionStorage` / `IAgentStateStorage` / `IUserStorage`，定义于 `ManInBlack.AI.Abstraction`），1–4 通过这些接口实现；本次迁移的核心是**用 SQLite 实现替换现有 `File*Storage` 实现，接口契约不变**。

## 2. 关键决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| 迁移范围 | 仅数据类 1–4 | 配置/profile/hook 留文件 |
| 后端策略 | SQLite 彻底替换 File | 唯一后端，删除 `File*Storage`；保留旧文件便于核对 |
| 存量数据 | 一次性迁移工具 | 显式命令导入旧 JSON |
| 访问库 | EF Core 10 + `Microsoft.EntityFrameworkCore.Sqlite` | Migrations、强类型映射、查询便利 |
| 复杂对象存储 | JSON Blob（TEXT 列）+ 少量可查列 | `ChatMessage` 多态、框架从不按内容做 SQL 查询，整批加载 |
| Schema 演进 | EF Core Migrations | `InitialCreate` + 未来平滑加字段 |
| Schema 启动 | 宿主显式 `MigrateAsync` | migrate 是启动期职责，存储层只读写 |
| 数据迁移触发 | 显式 `migrate-storage` 命令 | 与 schema migrate 哲学一致，全控、可预检、可回退 |

## 3. 架构

新增模块 `src/ManInBlack.AI/Persistence/`，用 EF Core + SQLite。新建 `SqliteAgentStateStorage`（实现 `IAgentStateStorage`）与 `SqliteUserStorage`（实现 `IUserStorage`），挂在现有接口后，**替换** `FileAgentStateStorage` / `FileUserStorage`。

单个 DB 文件落在可配置的 `{RootPath}/maninblack.db`（默认 `~/.man-in-black/maninblack.db`），**复用现有 `RootPath`，不新增配置项**。

**沙盒**：存储读写发生在主 Agent 进程（`PersistenceMiddleware`、`AgentFactory`），不在 bubblewrap 沙盒内，对 RootPath 的 DB 写入不受沙盒限制。实现时跑一次 demo 验证。

## 4. Schema

DB 文件：`{RootPath}/maninblack.db`，WAL 模式。三张表，对应原 `sessions/` + `users/` 两组文件。

### 4.1 `SessionMessages`（替换 `sessions/{id}.jsonl`）

| 列 | 类型 | 说明 |
|----|------|------|
| `Id` | INTEGER PK AUTOINCREMENT | 稳定主键，天然追加顺序 |
| `SessionId` | TEXT NOT NULL | 查询键 |
| `CreatedAt` | TEXT NOT NULL | ISO8601，便于排查/清理 |
| `PayloadJson` | TEXT NOT NULL | 整条 `ChatMessage` 序列化 |

索引：`(SessionId, Id)`——`LoadMessages` 按 session 拉取并排序。

### 4.2 `AgentStateSnapshots`（替换 `sessions/{id}.state.json`）

| 列 | 类型 | 说明 |
|----|------|------|
| `SessionId` | TEXT PK | 一个 session 一份快照（整存整取整覆盖） |
| `SavedAt` | TEXT NOT NULL | 抽出便于将来按时间清理老快照 |
| `PayloadJson` | TEXT NOT NULL | 整个 `AgentStateSnapshot` 序列化 |

### 4.3 `Users`（替换 `users/userIdMap.json` + `users/{id}.json`，折叠 map）

| 列 | 类型 | 说明 |
|----|------|------|
| `Id` | INTEGER PK AUTOINCREMENT | 对应当前 `SelfHostUserId`（数字字符串） |
| `UserId` | TEXT NOT NULL UNIQUE | 原始外部 id，查询键 |
| `MetadataJson` | TEXT NOT NULL | `UserEntry.Metadata`（`Dictionary<string,object>`） |
| `SessionIdsJson` | TEXT NOT NULL | `UserEntry.SessionIds`（`IList<string>`） |

**有意为之的简化**：当前是「`userIdMap.json`（原始id→数字id）+ `users/{数字id}.json`」两套结构 + 内存 `Interlocked` 自增计数器。EF 版合并为单张 `Users` 表：`UserId` 唯一索引做查询，`Id` 自增做内部编号（持久且并发安全，替代内存计数器）。语义不变，结构更直接。

### 4.4 EF 映射约定

- `PayloadJson` / `MetadataJson` / `SessionIdsJson` 用 `System.Text.Json` 在仓储类里显式序列化，与现有 `File*Storage` 写法一致，不引入 EF ValueConverter 隐式魔法。
- Entity 类放 `Persistence/Entities/`，与 Abstraction 领域模型（`UserEntry` / `AgentStateSnapshot`）隔离，仓储层负责互转。
- 初始 migration：`InitialCreate`，建 3 表 + 2 索引。

## 5. 组件与 DI 装配

```
src/ManInBlack.AI/Persistence/
├── Entities/
│   ├── SessionMessageEntity.cs      # Id / SessionId / CreatedAt / PayloadJson
│   ├── AgentStateSnapshotEntity.cs  # SessionId / SavedAt / PayloadJson
│   └── UserEntity.cs                # Id / UserId / MetadataJson / SessionIdsJson
├── ManInBlackDbContext.cs           # 3 个 DbSet + OnModelCreating(表名/索引/约束)
├── SqliteAgentStateStorage.cs       # : IAgentStateStorage，挂 ISessionStorage
├── SqliteUserStorage.cs             # : IUserStorage
└── JsonToSqliteMigrator.cs          # 一次性迁移（见 §7）
```

### 5.1 连接 / 上下文管理

- 在 `DependencyInjection.cs` 用 `AddDbContextFactory<ManInBlackDbContext>` 注册。连接串从 `AgentStorageOptions.RootPath` 取，复用现有配置，不新增配置项：

  ```csharp
  services.AddDbContextFactory<ManInBlackDbContext>((sp, o) =>
  {
      var root = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.RootPath;
      Directory.CreateDirectory(root);
      o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")}");
  });
  ```

- **DbContext 非线程安全**，而现有 File 存储是 Singleton、内部靠锁跨线程。改用 `IDbContextFactory`（工厂本身单例）：每次操作 `using var db = _factory.CreateDbContext();` 取一个短生命周期上下文。这是 EF 标准用法，顺带消掉原来 `JsonFileDictionary` 那套 `ReaderWriterLockSlim` 手写锁。

### 5.2 注册替换

- `SqliteAgentStateStorage` 打 `[ServiceRegister.Singleton.As<ISessionStorage>]`（实现 `IAgentStateStorage`），`SqliteUserStorage` 打 `[ServiceRegister.Singleton.As<IUserStorage>]`——与现有 File 实现的注册特性写法一致。
- `DependencyInjection.cs` 中 `IAgentStateStorage → ISessionStorage` 的映射保留不动。
- **删除**：`Services/FileSessionStorage.cs`、`Services/FileUserStorage.cs`、`Utils/JsonFileDictionary.cs`、`Utils/JsonFileList.cs`。

### 5.3 行为映射（接口语义零变化）

| 接口方法 | EF 实现 |
|---|---|
| `SaveMessage` | `INSERT` 一行（`PayloadJson`=序列化 ChatMessage，`CreatedAt`=now） |
| `LoadMessages` | `SELECT PayloadJson WHERE SessionId=? ORDER BY Id` → 逐条反序列化 |
| `LoadSnapshotAsync` | `SELECT` 单行 → 反序列化 / 无则 null |
| `SaveSnapshotAsync` | 按 `SessionId` upsert（load 后 add 或 update + SaveChanges） |
| `DeleteSnapshotAsync` | `DELETE WHERE SessionId=?` |
| `GetOrCreateUser` | 按 `UserId` 查；无则 INSERT，取自增 `Id` → `SelfHostUserId=Id.ToString()` |
| `SaveUserAsync` | 按 `UserId` load，更新两个 JSON 列，SaveChanges |
| `CreateNewSessionIdAsync` | GetOrCreateUser → 拼 `{userId}_{unixSec}` 追加 → save（逻辑同现在） |

### 5.4 Schema 启动（宿主显式）

- 提供扩展方法供宿主在 `Program.cs` 启动时调用一次：

  ```csharp
  // 内部：解析 IDbContextFactory<ManInBlackDbContext> → MigrateAsync → 设 PRAGMA journal_mode=WAL
  await app.Services.MigrateManInBlackStorageAsync();
  ```

- 存储类假定 schema 已就绪，构造时不再做任何 DB 操作，纯靠 `IDbContextFactory` 取上下文。
- 在 `demo/AgentConsole`、`demo/FeishuAdaptor` 的 `Program.cs` 接上这一行，并写进 docs。
- 每次启动跑一遍 `MigrateAsync`（已最新时是空操作、几乎零开销），DB 始终自动保持最新。

## 6. 错误处理与并发

**并发**：
- DbContext 非线程安全 → 每次操作各自 `using var db = _factory.CreateDbContext()`，跨线程不共享实例。
- SQLite **WAL** 模式：读不阻塞写；写串行（SQLite 固有限制）。
- `busy_timeout`（经一个极小的 `DbConnectionInterceptor` 在每连接执行 `PRAGMA busy_timeout=5000`）：FeishuAdaptor 多用户并发写时，抢不到写锁的重试几秒而非立刻抛 `SQLITE_BUSY`。
- 每条 `SaveMessage` 是独立短事务（INSERT）；`SaveUserAsync` 是 read-modify-write。现有 File 实现对同一用户并发改 `SessionIds` 本就是 best-effort（无 per-user 锁），SQLite 版语义持平，不加乐观锁 token。如实记录此限制。

**错误处理**：
- 损坏数据：迁移和 `LoadMessages` 遇到单行反序列化失败 → 记日志跳过该行，不整体崩（对齐现在 `LoadSnapshotAsync` 捕获 `JsonException`、`LoadMessages` 跳过 null 行的行为）。
- 迁移按数据类型各包一个事务，某批失败回滚该批并报错，DB 保持可用。
- 全新部署无旧 JSON 目录：`migrate-storage` 视为"无数据可迁"，汇总归零、建空 DB、正常退出（干净机器上跑也是安全 no-op）。

## 7. 一次性迁移工具（JSON → SQLite）

新增 `Persistence/JsonToSqliteMigrator.cs`，依赖 `IDbContextFactory<ManInBlackDbContext>` + `IOptions<AgentStorageOptions>`（拿 RootPath）。方法 `MigrateAsync(ct)` 返回汇总（各类导入/跳过计数）。

### 7.1 流程（每步用 EF 事务包住，失败回滚）

1. 先 `MigrateManInBlackStorageAsync()` 确保 schema 就位。
2. **会话历史**：扫 `{RootPath}/sessions/*.jsonl`。`sessionId`=去 `.jsonl` 的文件名；幂等（该 session 已有行则跳过）；逐行反序列化 → INSERT。原 JSONL 无时间戳，`CreatedAt` 记迁移时刻。
3. **状态快照**：扫 `{RootPath}/sessions/*.state.json`。`sessionId`=去 `.state.json`；幂等（已存在跳过）；反序列化 → upsert，`SavedAt` 取快照自带值。
4. **用户**：读 `{RootPath}/users/userIdMap.json`（`Dictionary<原始id, 数字id>`）+ 对应 `users/{数字id}.json`（`UserEntry`）。幂等（`UserId` 已存在跳过）；**保留原数字内部 id**，INSERT 时显式写入 `Id = int.Parse(数字id)`（保持 `SelfHostUserId` 不变）。需注意 SQLite 显式插入自增列后 `sqlite_sequence` 跟上，实现时验证后续自增正常。

### 7.2 迁移后处理

旧 `sessions/`、`users/` 目录**原地保留不删**（避免误删、便于核对）；工具只打日志汇总。用户确认无误后自行删除。因为幂等，重复运行安全。

### 7.3 触发方式

`demo/AgentConsole` 与 `demo/FeishuAdaptor` 均支持 `migrate-storage` 启动参数，复用同一个 `JsonToSqliteMigrator`。

- AgentConsole：`dotnet run --project demo/AgentConsole -- migrate-storage`
- FeishuAdaptor：`/opt/mib-feishu/FeishuAdaptor migrate-storage`

FeishuAdaptor 是 `WebApplication`（`app.Run()` 阻塞）。挂参数的标准做法：在 `Program.cs` 开头判断 `args[0]=="migrate-storage"`，构建 host → 跑迁移 → `return` 退出（不进 `app.Run()`，不连飞书）。

### 7.4 阿里云迁移 runbook

阿里云 RootPath = `/root/.man-in-black/`，数据在 `/root/.man-in-black/sessions/` + `users/`；systemd 服务 `mib-feishu.service`。停机 = 一次迁移耗时（个人 bot 秒级）：

```bash
# 1. 发新二进制(含 SQLite 存储 + migrator + migrate-storage 参数)到服务器
scp mib-feishu.tar.gz aliyun:~ && ssh aliyun 'tar xzf mib-feishu.tar.gz -C /opt/mib-feishu && chmod -R 755 /opt/mib-feishu'
# 2. 停服
ssh aliyun 'systemctl stop mib-feishu'
# 3. 迁移:读 /root/.man-in-black/sessions + users → 生成 maninblack.db
ssh aliyun '/opt/mib-feishu/FeishuAdaptor migrate-storage'
# 4. 核对(journalctl 看汇总计数 / ls 确认 maninblack.db 生成)
# 5. 起服
ssh aliyun 'systemctl start mib-feishu'
```

旧 JSON 原地保留，确认无误后手动删。

## 8. 测试与文档

### 8.1 测试（xunit + 手写 fake，遵循约定）

- 现有大多数测试用内存 `FakeStorage`，**不受后端切换影响**，保持绿。
- `CheckpointTests.cs:189` 直接 `new FileAgentStateStorage(...)`——改为对临时 SQLite 文件的 `SqliteAgentStateStorage`，顺带给新存储真实覆盖。
- 新增 `SqliteAgentStateStorage` / `SqliteUserStorage` 测试：消息往返与顺序（含 function/tool 消息）、快照存/读/删/覆盖、用户幂等与 `SelfHostUserId` 分配、`CreateNewSessionIdAsync` 追加、多线程并发 `SaveMessage` 不损坏。用 SQLite 共享内存连接或临时文件隔离。
- 新增 `JsonToSqliteMigrator` 测试：造样例 JSONL / `state.json` / users JSON → 迁移 → 断言行数；跑两次验证幂等；缺目录验证 no-op。

### 8.2 文档（遵循"改模块同步更新 docs/"）

- 新增 `docs/storage-guide.md`：SQLite 存储、`maninblack.db`、schema、启动期 `MigrateManInBlackStorageAsync()`、两 demo + FeishuAdaptor 的 `migrate-storage` 命令。
- 改 `docs/configuration-guide.md`：注明 RootPath 现含 DB 文件（无新配置键）。
- 改 `docs/architecture.md`：存储层改为 EF Core/SQLite。
- 阿里云迁移 runbook 写进 `docs/feishu-guide.md`（或 storage-guide）。
- `CLAUDE.md` 文档索引补一行 storage-guide。

## 9. 范围外

- 不迁移 5–7（settings.json / profile.md / mib-hooks.json）。
- 不引入存储后端切换（不做 `Storage.Backend` 配置项），SQLite 是唯一后端。
- 不为并发用户写加乐观锁 token（持平现有 best-effort 语义）。
- 迁移后旧 JSON 目录不自动删除。

## 10. 风险与验证点

- **SQLite 显式插入自增列后 `sqlite_sequence`**：迁移用户时显式写 `Id`，需验证后续新用户的自增 `Id` 不与已迁移值冲突。
- **bubblewrap 沙盒**：实现后跑 demo 验证主进程对 `{RootPath}/maninblack.db` 的写入不受沙盒影响（预期不受影响，存储不在沙盒内）。
- **FeishuAdaptor 生产环境**：阿里云迁移前先在本地或服务器副本上用生产 JSON 副本试跑一次，确认计数与数据完整后再正式操作。
- **`ChatMessage` 序列化兼容性**：`Microsoft.Extensions.AI` 的 `ChatMessage` 含多态 `AIContent`，确认 `System.Text.Json` 默认序列化能正确往返（现有 JSONL 已在用，应无问题，测试覆盖 function/tool 消息确认）。

### 10.1 端到端验证（AgentConsole 对话 + 查库）

实现完成后用 `demo/AgentConsole` 做真实验证（复用项目 `test-agent-console` skill 覆盖的完整流程）：

1. 跑几轮 AgentConsole 对话（含普通对话 + 触发工具调用，让 `SessionMessages` 覆盖 text 与 function/tool 两类 `AIContent`）。
2. 重启后再开同一 session，确认历史能从 SQLite 正确加载（`LoadMessages` 往返无损）。
3. 用 `sqlite3` CLI 直接查库核对落盘（只读，无需 GUI）：
   ```bash
   sqlite3 ~/.man-in-black/maninblack.db "SELECT SessionId, COUNT(*) FROM SessionMessages GROUP BY SessionId;"
   sqlite3 ~/.man-in-black/maninblack.db "SELECT SessionId, SavedAt FROM AgentStateSnapshots;"
   sqlite3 ~/.man-in-black/maninblack.db "SELECT Id, UserId FROM Users;"
   ```
4. 确认 `~/.man-in-black/sessions/`、`users/` 旧 JSON 目录**不再产生新文件**（新数据只进 DB）。

可用工具（已确认本机就位）：`sqlite3` 3.53.1（查库）、`dotnet-ef` 10.0.1（Migrations）。
