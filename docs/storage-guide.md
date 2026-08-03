# 存储指南

本文档介绍 ManInBlack 的持久化存储层：SQLite 后端（EF Core 10）、数据库结构、启动迁移、一次性数据导入工具。

---

## 概述

ManInBlack 使用 **SQLite**（通过 EF Core 10 + `Microsoft.EntityFrameworkCore.Sqlite`）存储所有运行期数据，包括会话消息、状态快照和用户数据。DB 文件位于 `{AgentStorageOptions.RootPath}/maninblack.db`，默认路径为 `~/.man-in-black/maninblack.db`。

无需新增配置键——`RootPath` 已有的默认值（`~/.man-in-black`）即为 DB 所在目录。

> 可用 [Dashboard](dashboard-guide.md) demo 在浏览器查看库内会话消息与用户。

---

## 数据库结构

DB 包含 4 张表。会话经 `Sessions` 表为一等实体（`SessionMessages`/`AgentStateSnapshots` 通过 `SessionId` 外键指向它，`onDelete: Cascade`）。

### Users（用户）

| 列         | 类型      | 说明                                   |
| ---------- | --------- | -------------------------------------- |
| `Id`       | 自增 PK   | 内部编号（对应 `SelfHostUserId`）      |
| `UserId`   | TEXT      | 原始外部 ID（唯一索引）                |
| `CreatedAt`| `DateTime` | 用户创建时间（ISO-8601 字符串存储）   |

### Sessions（会话，一等实体）

| 列         | 类型        | 说明                                                          |
| ---------- | ----------- | ------------------------------------------------------------- |
| `Id`       | 自增 PK     | 行标识                                                        |
| `SessionId`| TEXT        | 会话 ID（**唯一约束** `AK_Sessions_SessionId`）               |
| `UserId`   | INTEGER FK  | 归属用户 → `Users.Id`（`onDelete: Cascade`）                  |
| `Source`   | INTEGER     | 会话来源：`SessionSource` 枚举（见下）                        |
| `CreatedAt`| `DateTime`  | 会话创建时间                                                  |
| `LastAt`   | `DateTime`  | 最近一条消息时间（`SaveMessage` 时更新）                      |

索引：`SessionId` 唯一索引、`UserId` 索引。

> `SessionSource` 枚举（`Interactive=0`、`Webhook=1`）：`Sessions.Source` 区分用户交互会话（飞书 IM 等）与自动化触发会话。`GetLatestSessionIdAsync(userId, source)` 按 `Source` 过滤取最近会话，确保不同触发源互不串扰（例如 Webhook 会话不会顶掉用户的 Interactive 会话）。

### SessionMessages（会话消息）

| 列            | 类型        | 说明                                         |
| ------------- | ----------- | -------------------------------------------- |
| `Id`          | 自增 PK     | 行标识                                       |
| `SessionId`   | TEXT FK     | 会话 ID → `Sessions.SessionId`（`onDelete: Cascade`） |
| `CreatedAt`   | `DateTime`  | 创建时间                                     |
| `PayloadJson` | TEXT        | 完整 `ChatMessage` 序列化 JSON               |

索引：`(SessionId, Id)` 联合索引，加速按会话查询。

### AgentStateSnapshots（状态快照）

| 列            | 类型        | 说明                                                  |
| ------------- | ----------- | ----------------------------------------------------- |
| `SessionId`   | PK TEXT FK  | 会话 ID → `Sessions.SessionId`（每个会话最多一条；`onDelete: Cascade`） |
| `SavedAt`     | `DateTime`  | 快照保存时间                                          |
| `PayloadJson` | TEXT        | 完整 `AgentStateSnapshot` 序列化 JSON                 |

> 引用完整性：`SessionMessages.SessionId` 与 `AgentStateSnapshots.SessionId` 均为指向 `Sessions.SessionId` 的外键，级联删除。删除一个会话行会一并清除其消息与快照；写入消息/快照前必须先有对应的 `Sessions` 行。

复杂对象（`ChatMessage`、`AgentStateSnapshot`）以 JSON blob 存储在 TEXT 列中（仅这两张表的 `PayloadJson`；用户/会话的关系结构已正规化为独立表与列，不再有 `MetadataJson`/`SessionIdsJson` blob）。

---

## WAL 模式与并发

- **WAL 模式**（`PRAGMA journal_mode=WAL`）—— 启动期由 `MigrateManInBlackStorageAsync()` 设置一次，是库级持久设置。
- **busy_timeout=5000**（5 秒）—— 通过 `SqliteInitInterceptor` 在每个连接打开时设置。并发写抢锁时 SQLite 会自动重试，而非立刻抛出 `SQLITE_BUSY`。

这两个设置确保多线程/多进程场景下的写入安全性。

---

## 启动迁移

宿主在 `BuildServiceProvider()` 之后调用一次即可：

```csharp
var rootSp = services.BuildServiceProvider();
await rootSp.MigrateManInBlackStorageAsync();
```

`MigrateManInBlackStorageAsync()` 做两件事：

1. **应用 EF Core 迁移**（`Database.MigrateAsync`）。旧 `Users.SessionIdsJson` blob → `Sessions` 行的数据搬迁**内置于 `NormalizeSessionsFinalize` migration 的 `Up`**（`migrationBuilder.Sql` + SQLite `json_each`，在加 FK / 删 blob 列之前执行）：blob 里每个 sessionId 转成一行 `Sessions`（`Source=Interactive`，`CreatedAt` 从 `{userId}_{unix秒}` 解析，`LastAt` 取该会话最晚消息时间）；不在任何 blob 里的孤儿 sessionId，其 `SessionMessages`/`AgentStateSnapshots` 在加 FK 前被删除。`Down` 用 `json_group_array(json_quote(...))` 反向重建 blob。因此 `dotnet ef database update` 与 app 启动**都**能完整升级，无需分阶段启动逻辑。
2. 设置 WAL 模式。

> **注意：** 必须在无事务的上下文中调用，WAL pragma 只能在无事务时设置。数据搬迁 SQL 在 Finalize 的 `Up` 内、删 blob 列之前执行，故 blob 列此刻仍存在。

---

## EF Migrations

当前迁移：

| 迁移名称                    | 说明                                                                 |
| --------------------------- | -------------------------------------------------------------------- |
| `InitialCreate`             | 创建 `Users`/`SessionMessages`/`AgentStateSnapshots` 三表及索引       |
| `NormalizeSessionsPrep`     | 新增 `Sessions` 表 + `Users.CreatedAt`（additive，向前兼容）         |
| `NormalizeSessionsTimeTypes`| 时间列改 `DateTime`（数据搬迁在这一步之后、Finalize 之前执行）       |
| `NormalizeSessionsFinalize` | 数据搬迁（blob→Sessions，`json_each`）+ 孤儿清理 + 加 `Sessions.SessionId` 唯一约束 + FK→Sessions + 删 `Users` blob 列 |

迁移文件位于 `src/ManInBlack.AI/Persistence/Migrations/`。

---

## 实现类

| 类                          | 实现接口                  | 说明                                        |
| --------------------------- | ------------------------- | ------------------------------------------- |
| `SqliteAgentStateStorage`   | `IAgentStateStorage`      | 会话消息 + 状态快照读写                     |
| `SqliteUserStorage`         | `IUserStorage`            | 用户 + `Sessions` 读写（建会话/取最近会话） |
| `ManInBlackDbContext`       | `DbContext`               | EF Core 上下文                              |
| `JsonToSqliteMigrator`      | 一次性迁移工具            | JSON 文件 → SQLite 导入（含 `Sessions` 行） |
| `SqliteInitInterceptor`     | `DbConnectionInterceptor` | 设置 busy_timeout                           |

所有存储实现使用 `IDbContextFactory<ManInBlackDbContext>`（工厂本身为 Singleton），每次操作创建短生命周期上下文，符合 EF 标准用法。

---

## 一次性迁移（JSON → SQLite）

从旧版 JSON 文件格式导入数据到 SQLite。

### 用法

```bash
# AgentConsole
dotnet run --project demo/AgentConsole -- migrate-storage

# FeishuAdaptor
./FeishuAdaptor migrate-storage
```

### 迁移内容

导入顺序：**先用户 + 其会话**（`Users` 行 + `Sessions` 行），再会话消息/状态快照。`SessionMessages`/`AgentStateSnapshots` 的 FK 要求对应 `Sessions` 行必须先存在，因此归属按 sessionId 的 `{userId}_` 前缀解析。

| 数据类型   | 旧文件路径                                             | 新位置                              |
| ---------- | ------------------------------------------------------ | ----------------------------------- |
| 用户数据   | `{RootPath}/users/userIdMap.json` + `users/{id}.json`  | `Users` 表（合并两张旧文件）         |
| 用户会话   | 旧条目里的 `SessionIds`                                | `Sessions` 行（不再写 blob）         |
| 会话消息   | `{RootPath}/sessions/{sessionId}.jsonl`                | `SessionMessages` 表（需先有 Sessions 行） |
| 状态快照   | `{RootPath}/sessions/{sessionId}.state.json`           | `AgentStateSnapshots` 表（需先有 Sessions 行） |

### 特性

- **幂等**：按 `SessionId` / `UserId` 判断，已存在的记录跳过。可安全重复运行。
- **保留原 ID**：用户内部编号（`Id`）在导入时显式写入，确保 `SelfHostUserId` 不变。
- **无主会话跳过**：旧 sessions 文件的 sessionId 若不含可解析的 `{userId}_` 前缀（或前缀对不上任何已导入用户），无法补建 `Sessions` 行以满足 FK——该会话的消息/快照被**跳过并记一条 warning**（`Skipped` 计数 +1，不写 `Sessions`/`SessionMessages`/`AgentStateSnapshots` 行，整体导入不抛）。**原始数据仍保留在源 JSON 文件中**，可后续核对/手动归属。
- **旧文件不删**：`sessions/` 和 `users/` 目录原地保留，便于核对。确认无误后可手动删除。
- **输出示例**：`迁移完成:消息 42,快照 7,用户 3,跳过 0`

---

## 旧存储格式（已废弃）

以下旧文件和实现已删除，仅供迁移参考：

| 旧文件/类                  | 说明                             |
| -------------------------- | -------------------------------- |
| `sessions/*.jsonl`         | 旧会话消息（JSONL 追加）        |
| `sessions/*.state.json`    | 旧状态快照（JSON 原子替换）     |
| `users/userIdMap.json`     | 旧用户 ID 映射                  |
| `users/{id}.json`          | 旧用户条目                      |
| `FileAgentStateStorage`    | 旧存储实现（已删除）             |
| `FileUserStorage`          | 旧用户存储实现（已删除）         |
| `JsonFileDictionary`       | 旧 JSON 字典工具（已删除）       |
| `JsonFileList`             | 旧 JSON 列表工具（已删除）       |

新代码不再向 `sessions/` 和 `users/` 目录写入任何文件。
