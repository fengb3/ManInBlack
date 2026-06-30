# 存储指南

本文档介绍 ManInBlack 的持久化存储层：SQLite 后端（EF Core 10）、数据库结构、启动迁移、一次性数据导入工具。

---

## 概述

ManInBlack 使用 **SQLite**（通过 EF Core 10 + `Microsoft.EntityFrameworkCore.Sqlite`）存储所有运行期数据，包括会话消息、状态快照和用户数据。DB 文件位于 `{AgentStorageOptions.RootPath}/maninblack.db`，默认路径为 `~/.man-in-black/maninblack.db`。

无需新增配置键——`RootPath` 已有的默认值（`~/.man-in-black`）即为 DB 所在目录。

> 可用 [Dashboard](dashboard-guide.md) demo 在浏览器查看库内会话消息与用户。

---

## 数据库结构

DB 包含 3 张表：

### SessionMessages（会话消息）

| 列            | 类型  | 说明                                         |
| ------------- | ----- | -------------------------------------------- |
| `Id`          | 自增 PK | 行标识                                     |
| `SessionId`   | TEXT  | 会话 ID                                     |
| `CreatedAt`   | TEXT  | 创建时间                                     |
| `PayloadJson` | TEXT  | 完整 `ChatMessage` 序列化 JSON               |

索引：`(SessionId, Id)` 联合索引，加速按会话查询。

### AgentStateSnapshots（状态快照）

| 列            | 类型    | 说明                                     |
| ------------- | ------- | ---------------------------------------- |
| `SessionId`   | PK TEXT | 会话 ID（每个会话最多一条快照）           |
| `SavedAt`     | TEXT    | 快照保存时间                             |
| `PayloadJson` | TEXT    | 完整 `AgentStateSnapshot` 序列化 JSON    |

### Users（用户）

| 列               | 类型  | 说明                                           |
| ---------------- | ----- | ---------------------------------------------- |
| `Id`             | 自增 PK | 内部编号（对应 `SelfHostUserId`）             |
| `UserId`         | TEXT  | 原始外部 ID（唯一索引）                        |
| `MetadataJson`   | TEXT  | 用户元数据 JSON                                |
| `SessionIdsJson` | TEXT  | 关联会话 ID 列表 JSON                          |

复杂对象（`ChatMessage`、`AgentStateSnapshot`、用户元数据和会话列表）以 JSON blob 存储在 TEXT 列中。

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

1. 应用所有待执行的 EF Core 迁移（`InitialCreate` 等），自动建表。
2. 设置 WAL 模式。

> **注意：** 必须在无事务的上下文中调用，WAL pragma 只能在无事务时设置。

---

## EF Migrations

当前迁移：

| 迁移名称          | 说明                 |
| ----------------- | -------------------- |
| `InitialCreate`   | 创建 3 张表及索引     |

迁移文件位于 `src/ManInBlack.AI/Persistence/Migrations/`。

---

## 实现类

| 类                          | 实现接口                  | 说明                     |
| --------------------------- | ------------------------- | ------------------------ |
| `SqliteAgentStateStorage`   | `IAgentStateStorage`      | 会话消息 + 状态快照读写  |
| `SqliteUserStorage`         | `IUserStorage`            | 用户数据读写             |
| `ManInBlackDbContext`       | `DbContext`               | EF Core 上下文           |
| `JsonToSqliteMigrator`      | 一次性迁移工具            | JSON 文件 → SQLite 导入  |
| `SqliteInitInterceptor`     | `DbConnectionInterceptor` | 设置 busy_timeout        |

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

| 数据类型   | 旧文件路径                                           | 新位置                     |
| ---------- | ---------------------------------------------------- | -------------------------- |
| 会话消息   | `{RootPath}/sessions/{sessionId}.jsonl`              | `SessionMessages` 表      |
| 状态快照   | `{RootPath}/sessions/{sessionId}.state.json`         | `AgentStateSnapshots` 表  |
| 用户数据   | `{RootPath}/users/userIdMap.json` + `users/{id}.json` | `Users` 表（合并两张旧文件） |

### 特性

- **幂等**：按 `SessionId` / `UserId` 判断，已存在的记录跳过。可安全重复运行。
- **保留原 ID**：用户内部编号（`Id`）在导入时显式写入，确保 `SelfHostUserId` 不变。
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
