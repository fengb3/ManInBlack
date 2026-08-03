# 持久化层正规化（纯关系型）设计

> 日期：2026-08-03
> 状态：已批准（脑暴通过），待实现
> 范围：`src/ManInBlack.AI` 持久化层（Abstraction + Persistence）+ `demo/Dashboard` 查询 + EF 迁移 + 全测试。**不动** agent 管道/中间件/源生成器逻辑，只动它们消费的存储接口与 `AgentFactory.cs:179`。
> 关联：webhook 触发入口（后续特性，待正规化落地后再立项）将依赖本特性的 `Sessions.Source` 列。

## 背景与动机

持久化层是早期「JSON blob 塞列」风格，会话不是一等公民：用户会话列表是 `Users.SessionIdsJson` 里的 JSON 数组，SessionId→UserId 映射靠 Dashboard **全表扫所有用户、逐个反序列化 blob** 重建（`ChatHistoryQueries.cs:114-132`）。随着系统接入更多触发源（webhook 自动化、未来定时器/其他 IM），这套模型的局限暴露：

- 会话无来源标记 → 自动化触发的会话与用户交互会话混在一起，无法区分/审计。
- 无引用完整性（无 FK）→ 删会话留孤儿。
- 时间是字符串 → 排序靠字典序侥幸。

趁 webhook 要「会话可审计」这个需求，把持久化层理顺为纯关系型，让会话成一等公民。这是**独立于 webhook 的基础设施工程**，webhook 作为首个消费 `Sessions.Source` 的后续特性。

## 现状盘点（草台点）

3 张表（`ManInBlackDbContext.cs:11-46`）：

| 表 | 列 | 问题 |
|---|---|---|
| Users | Id, UserId(唯一), MetadataJson(blob), SessionIdsJson(blob) | 会话列表是 blob |
| SessionMessages | Id, SessionId(**无 FK**), CreatedAt(**string**), PayloadJson | 无引用完整性、时间靠字符串排序 |
| AgentStateSnapshots | SessionId(PK, **无 FK**), SavedAt(string), PayloadJson | 同上 |

5 处草台：

1. **无 Sessions 表** → 会话非一等公民。
2. **SessionId→UserId 映射靠反查 blob**（`ChatHistoryQueries.BuildSessionToUserMapAsync` `ChatHistoryQueries.cs:114-132` 全表扫所有用户）。
3. **`Users.MetadataJson`** blob（生产零使用，dead field）。
4. **CreatedAt/SavedAt 是 string**（`SessionMessageEntity.cs:10`、`AgentStateSnapshotEntity.cs:9`）。
5. **SessionMessages/AgentStateSnapshots 的 SessionId 无 FK**。

## 可行性证据（已核实）

| 项 | 证据 |
|---|---|
| EF migration 在用 | `ManInBlackDbContextModelSnapshot.cs` 存在；启动期 `app.Services.MigrateManInBlackStorageAsync()`（`Program.cs:95`）自动应用 EF migration |
| GetLatestSessionId 唯一生产调用 | `AgentFactory.cs:179`（扩展方法定义 `ISessionStorage.cs:75`） |
| CreateNewSessionIdAsync 调用 | `AgentFactory.cs:179` + `BuiltinCommands.cs:22`（`/new`、`/reset`） |
| Metadata 生产零写入 | 全仓仅 `SqliteUserStorageTests.cs:44` 写 `Metadata["role"]`；生产代码只序列化/反序列化（`SqliteUserStorage.cs:43,61`）+ Dashboard 读展示（`ChatHistoryQueries.cs:58`）→ dead field |
| SessionId 格式 | `{userId}_{Unix秒}`（`SqliteUserStorage.cs:51`），迁移时可解析 ts 填 CreatedAt |
| JsonToSqliteMigrator 同步面 | 写 `SessionIdsJson`（`JsonToSqliteMigrator.cs:162`），schema 变后需同步改写 Sessions 表 |

## 已定决策

| 维度 | 决定 |
|---|---|
| 深度 | **轻量正规化**：新建 Sessions 表 + 去 blob + FK + 时间类型 + Dashboard 简化；**PayloadJson（ChatMessage）保留 blob**（不拆消息体） |
| Metadata | **删除**（dead field） |
| 会话来源 | `Sessions.Source` 列（Interactive/Webhook）；`GetLatestSessionId` 默认只取 Interactive |
| webhook | 本特性不含；待正规化落地后再立项 |
| 旧数据 | EF migration + 数据搬迁（无手动脚本） |

### 为何不深度正规化（拆 PayloadJson）

ChatMessage 结构复杂（role/text/tool calls/function result 等），拆成正规消息列成本高；收益是「消息内容可 SQL 查询/索引」，但 Dashboard Search 当前 `LIKE PayloadJson`（`ChatHistoryQueries.cs:100`）够用，边际收益低。YAGNI。

## 目标 schema（纯关系型）

```
Users
  Id (PK, long 自增)        -- SelfHostUserId
  UserId (unique)           -- 外部业务 id
  CreatedAt (DateTime, 新增)
  —— 删除 MetadataJson / SessionIdsJson

Sessions (新建，会话成一等公民)
  Id (PK, long 自增)
  SessionId (unique)        -- 业务键 {userId}_{ts}
  UserId (FK → Users.Id)
  Source (int)              -- 0=Interactive, 1=Webhook（可扩展）
  CreatedAt (DateTime)
  LastAt (DateTime)         -- 最后活动时间，会话列表排序用

SessionMessages
  Id (PK, long 自增)
  SessionId (FK → Sessions.SessionId)   —— 加 FK（沿用字符串 SessionId，代码改动最小）
  CreatedAt (DateTime)                  —— string → DateTime
  PayloadJson (blob)                    —— ChatMessage，保留

AgentStateSnapshots
  SessionId (PK, FK → Sessions.SessionId)   —— 加 FK
  SavedAt (DateTime)                        —— string → DateTime
  PayloadJson (blob)
```

- **FK 用 `SessionId`（字符串）指向 `Sessions.SessionId`（唯一索引）**，而非代理键 `Sessions.Id` —— 代码到处用 SessionId 字符串，改动最小。
- **`Sessions.LastAt`**：写消息时更新，便于 Dashboard 会话列表按最后活动排序，取代现在 `GroupBy + Max(CreatedAt)` 的实时聚合。

## 设计 / 组件改动

### 1. Entities（`src/ManInBlack.AI/Persistence/Entities/`）
- **新增** `SessionEntity.cs`：`{ Id, SessionId, UserId(long), Source(int), CreatedAt(DateTime), LastAt(DateTime) }`。
- **改** `UserEntity.cs`：删 `MetadataJson`/`SessionIdsJson`，加 `CreatedAt(DateTime)`。
- **改** `SessionMessageEntity.cs`：`CreatedAt` string→DateTime（SessionId 列保留，加 FK）。
- **改** `AgentStateSnapshotEntity.cs`：`SavedAt` string→DateTime（SessionId 加 FK）。

### 2. `ManInBlackDbContext.cs`
- 加 `DbSet<SessionEntity> Sessions`。
- `Sessions`：PK `Id`、`SessionId` 唯一索引、`UserId` FK→Users、`Source`/`CreatedAt`/`LastAt`。
- `SessionMessages`：`SessionId` FK→`Sessions.SessionId`、`CreatedAt` DateTime。
- `AgentStateSnapshots`：`SessionId` FK→`Sessions.SessionId`、`SavedAt` DateTime。
- `Users`：删两 blob 列、加 `CreatedAt`。

### 3. 存储接口（`src/ManInBlack.AI.Abstraction/Storage/`）
- **删** `UserEntry.SessionIds`、`UserEntry.Metadata`、`UserEntryExtensions.GetLatestSessionId`（`UserEntry` 瘦身为 `{ UserId, SelfHostUserId, CreatedAt }`）。
- **新增** `SessionSource` enum（`Interactive=0, Webhook=1`）。
- **`IUserStorage`**：
  - `CreateNewSessionIdAsync(string userId, SessionSource source = Interactive)` → 写 Sessions 表、返回 SessionId。
  - **新增** `GetLatestSessionIdAsync(string userId, SessionSource source = Interactive)` → 查 Sessions 表（`OrderByDescending(LastAt)`），无则 null。
  - `GetOrCreateUser`/`SaveUserAsync` 保留（已无 SessionIds/Metadata 可存）。
- `ISessionStorage`（`SaveMessage`/`LoadMessages`，按 SessionId）不变；`IAgentStateStorage` 不变。

### 4. `AgentFactory.cs:179`
```
agentContext.SessionId = await userStorage.GetLatestSessionIdAsync(rootUserId, SessionSource.Interactive)
                        ?? await userStorage.CreateNewSessionIdAsync(rootUserId, SessionSource.Interactive);
```
（原 `user.GetLatestSessionId()` 同步扩展方法删除，改为 await 查表。）

### 5. `BuiltinCommands.cs:22`
`/new`、`/reset` 的 `CreateNewSessionIdAsync(context.ParentId)` → 加 `SessionSource.Interactive`（默认值即兼容，显式更清晰）。

### 6. `SqliteUserStorage.cs`
- 去 `MetadataJson`/`SessionIdsJson` 序列化（`SaveUserAsync` `:43-44`、`ToEntry` `:61-62`）。
- `CreateNewSessionIdAsync` 改为写 `Sessions` 表（生成 `{userId}_{ts}` + Source + CreatedAt + LastAt）。
- 新增 `GetLatestSessionIdAsync`（查 Sessions 表）。

### 7. 写消息更新 `Sessions.LastAt`
`SaveMessage`（`SqliteAgentStateStorage`）写 SessionMessage 后，`UPDATE Sessions SET LastAt=@now WHERE SessionId=@sid`。会话行由 `CreateNewSessionIdAsync` 预先建立；webhook 入口同样先建（后续衔接时）。若遇未建行的边界，补建一行 Source=Interactive 兜底（实现期确认是否需要）。

### 8. `demo/Dashboard/Data/ChatHistoryQueries.cs`
- `ListSessionsAsync`：直接查 `Sessions` 表（join `Users` 取 UserId、按 `LastAt` 排序），**删掉** `GroupBy SessionMessages` 实时聚合与 `BuildSessionToUserMapAsync` 全表扫。
- `ListUsersAsync`：去 Metadata/SessionIds 反序列化。
- `GetSessionMessagesAsync`/`SearchAsync`：不变（仍查 SessionMessages，`LIKE PayloadJson`）。
- 可选：会话列表展示 Source 标记。

### 9. `JsonToSqliteMigrator.cs`
从旧 JSON 文件迁到 SQLite，现写 `SessionIdsJson`（`:162`）。schema 变后改为：为每个 sessionId 写一行 `Sessions`（Source=Interactive，CreatedAt 从 `{userId}_{ts}` 解析）。同步更新，否则旧 JSON 数据无法迁入。

## 数据迁移

**EF Core migration 自动 handle schema 变更**（建表、加/删列、改列类型、加 FK 约束），但**不自动 handle JSON blob 的数据搬迁**——它不解析 `SessionIdsJson` 内容，不知道「数组要拆成多行」。blob→表拆分必须手写，两条路：

- **C# 搬迁（推荐）**：启动期跑，STJ 反序列化 `SessionIdsJson` → 写 `Sessions` 行。清晰、可单测、不依赖 SQLite JSON 函数版本，符合项目已有启动期迁移传统（`MigrateManInBlackStorageAsync` `Program.cs:95` + `JsonToSqliteMigrator`）。
- SQL 搬迁（`migrationBuilder.Sql` + SQLite `json_each`）：一步到位，但 SQL 复杂（解析 `{userId}_{ts}` 的 ts、算 LastAt）、依赖 SQLite JSON 函数（3.38+ 内置，需核实 `Microsoft.Data.Sqlite` 捆绑版本）、难测试。不推荐。

**编排（走 C# 搬迁）**——分阶段，保证 FK 约束在数据就绪后才加：

1. **migration 1（准备）**：建 `Sessions` 表 + 加 `Users.CreatedAt` + 改 `SessionMessages.CreatedAt`/`AgentStateSnapshots.SavedAt` 为 DateTime。**暂不加 FK、暂不删 blob**。
2. **C# 搬迁**（启动期，`MigrateManInBlackStorageAsync` 流程内，**幂等**：检测「`Users.SessionIdsJson` 非空且 `Sessions` 为空」才跑）：读 `SessionIdsJson` → 每个 sessionId 写一行 `Sessions`（Source=Interactive、CreatedAt=解析 `{userId}_{ts}` 的 ts、LastAt=该会话消息 `Max(CreatedAt)`）；孤儿 sessionId（不在任何 `SessionIdsJson` 里但 SessionMessages 引用了）补建 Sessions 行。
3. **migration 2（收尾）**：加 `SessionMessages`/`AgentStateSnapshots` 的 FK→`Sessions.SessionId` + 删 `Users.MetadataJson`/`SessionIdsJson`。

开发库与生产库均走此流程（启动期自动）。两个 migration 之间的 C# 搬迁是幂等的，重跑安全。

## 测试策略

- `SqliteUserStorageTests`：重写——`CreateNewSessionIdAsync` 写 Sessions 表、`GetLatestSessionIdAsync` 按 Source 过滤、去 Metadata/SessionIds。
- 新 `SessionStorage` 相关测试：建会话、按 Source 查 latest、`LastAt` 更新。
- `ChatHistoryQueriesTests`：`ListSessionsAsync` 直接查 Sessions、不再全表扫；`ListUsersAsync` 去 Metadata。
- **迁移测试**：建旧 schema（含 `SessionIdsJson` blob + 旧字符串时间）→ 跑 migration → 断言 Sessions 行数 = 所有 sessionId、Source=Interactive、SessionMessages FK 完整、无孤儿。
- `AgentFactoryTests`：SessionId 解析改异步查表后的行为（`GetLatestSessionIdAsync` null 时新建 Interactive）。
- `JsonToSqliteMigrator` 测试：旧 JSON → 新 schema（Sessions 行）。
- 现有测试桩（`FakeStorage`、FeishuAdaptor 桩）同步新签名。

## 范围（不做 / YAGNI）

- 不拆 `PayloadJson`（ChatMessage 保留 blob）——深度正规化留作未来。
- 不动 agent 管道/中间件/源生成器逻辑（只动存储接口 + `AgentFactory.cs:179`）。
- 不做 webhook 入口（后续特性，衔接 `Sessions.Source`）。
- 不做会话软删除/归档（只保证删会话不孤儿；删除策略后续）。

## 后续

- **webhook 触发入口**：后续特性，待本特性落地后再设计；届时 `CreateNewSessionIdAsync(userId, Webhook)` 直接产生可审计的会话。
- 触发源变多后，`SessionSource` 扩展（Timer/OtherIm…），`GetLatestSessionIdAsync` 默认 Interactive 的语义不变。
