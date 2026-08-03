# 持久化层正规化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 SQLite 持久化层从「JSON blob 塞列」正规化为纯关系型：会话成一等公民（新建 `Sessions` 表带 `Source` 标记），去 `MetadataJson`/`SessionIdsJson` blob，加 FK 引用完整性，时间列改 `DateTime`，Dashboard 查询简化。

**Architecture:** 新增 `SessionEntity` + `SessionSource` 枚举；`UserEntry` 瘦身（去 Metadata/SessionIds）；`IUserStorage.CreateNewSessionIdAsync` 写 `Sessions` 表、新增 `GetLatestSessionIdAsync`（按 `Source` 过滤）。两阶段 EF migration（Prep 建 `Sessions`+加列；Finalize 加 FK+删 blob），中间夹一个**幂等 C# 数据搬迁**（启动期 `MigrateManInBlackStorageAsync` 内，分阶段 migrate 以保证搬迁在两 migration 之间执行）。SQLite 的 `string`/`DateTime` 都映射 TEXT，时间类型切换对现有 ISO 数据无损。

**Tech Stack:** .NET 10, EF Core 10.0.0 (SQLite + Design), Microsoft.Extensions.AI (`ChatMessage`), xUnit。

**Spec:** `docs/superpowers/specs/2026-08-03-persistence-normalization-design.md`

---

## File Structure

**Create:**
- `src/ManInBlack.AI/Persistence/Entities/SessionEntity.cs` — 会话实体（一等公民）。
- `src/ManInBlack.AI/Persistence/NormalizeSessionsDataMigration.cs` — 启动期幂等数据搬迁（blob → `Sessions` 行）。
- `src/ManInBlack.AI/Persistence/Migrations/<ts>_NormalizeSessionsPrep.cs` — EF 生成（建 Sessions + Users.CreatedAt + 时间类型）。
- `src/ManInBlack.AI/Persistence/Migrations/<ts>_NormalizeSessionsFinalize.cs` — EF 生成（加 FK + 删 blob 列）。
- `test/ManInBlack.AI.Tests/Persistence/NormalizeSessionsDataMigrationTests.cs` — 搬迁测试（含孤儿）。
- `test/ManInBlack.AI.Tests/Persistence/SessionStorageNormalizationTests.cs` — `GetLatestSessionIdAsync`/`CreateNewSessionIdAsync` 按 Source 行为。

**Modify:**
- `src/ManInBlack.AI.Abstraction/Storage/ISessionStorage.cs` — `UserEntry` 瘦身、删 `GetLatestSessionId` 扩展、新增 `SessionSource`。
- `src/ManInBlack.AI.Abstraction/Storage/IUserStorage.cs` — `CreateNewSessionIdAsync(.., source)`、新增 `GetLatestSessionIdAsync`。
- `src/ManInBlack.AI/Persistence/Entities/{UserEntity,SessionMessageEntity,AgentStateSnapshotEntity}.cs` — 去 blob、时间类型、FK 列。
- `src/ManInBlack.AI/Persistence/ManInBlackDbContext.cs` — `Sessions` DbSet + 关系 + FK。
- `src/ManInBlack.AI/Persistence/SqliteUserStorage.cs` — 写 `Sessions`、`GetLatestSessionIdAsync`、去 blob 序列化。
- `src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs` — `SaveMessage` 更新 `LastAt`、时间 `DateTime`。
- `src/ManInBlack.AI/Persistence/StorageMigrationExtensions.cs` — 分阶段 migrate + 调搬迁。
- `src/ManInBlack.AI/Persistence/JsonToSqliteMigrator.cs` — 写 `Sessions` 行、去 blob、时间 `DateTime`。
- `src/ManInBlack.AI/AgentFactory.cs:179` — `await GetLatestSessionIdAsync`。
- `src/ManInBlack.AI/Commands/BuiltinCommands.cs:22` — 显式 `SessionSource.Interactive`。
- `demo/Dashboard/Data/ChatHistoryQueries.cs` — 查 `Sessions`、删 `BuildSessionToUserMapAsync`。
- `demo/Dashboard/Data/ReadModels.cs` — `UserSummary` 去 Metadata/SessionIds；`SessionSummary` 加 `Source`。
- `test/ManInBlack.AI.Tests/Helpers/FakeStorage.cs` — `FakeUserStorage` 新签名。
- `test/ManInBlack.AI.Tests/Persistence/SqliteUserStorageTests.cs` — 重写。
- `test/Dashboard.Tests/ChatHistoryQueriesTests.cs` — 适配。
- `docs/storage-guide.md` — schema 说明。

---

## Task 1: SessionSource + SessionEntity + UserEntity.CreatedAt + DbContext Sessions（additive）

**目标：** 纯加法引入 `Sessions` 表与 `SessionSource`，不动旧 blob、不改接口。编译绿。

**Files:**
- Create: `src/ManInBlack.AI/Persistence/Entities/SessionEntity.cs`
- Modify: `src/ManInBlack.AI.Abstraction/Storage/ISessionStorage.cs`（仅追加 `SessionSource` enum，不删任何东西）
- Modify: `src/ManInBlack.AI/Persistence/Entities/UserEntity.cs`
- Modify: `src/ManInBlack.AI/Persistence/ManInBlackDbContext.cs`
- Generate: `Migrations/<ts>_NormalizeSessionsPrep.cs`

- [ ] **Step 1: 在 `ISessionStorage.cs` 顶部追加 `SessionSource` enum**（`UserEntry` 暂不动）

在 `namespace ManInBlack.AI.Abstraction.Storage;` 下方、`ISessionStorage` interface 之前插入：

```csharp
/// <summary>会话来源：区分用户交互会话与自动化触发会话。</summary>
public enum SessionSource
{
    /// <summary>用户交互（飞书 IM 等）。</summary>
    Interactive = 0,
    /// <summary>自动化 webhook 触发。</summary>
    Webhook = 1,
}
```

- [ ] **Step 2: 新建 `SessionEntity.cs`**

```csharp
namespace ManInBlack.AI.Persistence.Entities;

/// <summary>会话实体（正规化后的一等公民）。</summary>
public sealed class SessionEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public long UserId { get; set; }
    /// <summary>关联的 <see cref="UserEntity.Id"/>（SelfHostUserId）。</summary>
    public UserEntity User { get; set; } = null!;
    public int Source { get; set; }   // SessionSource
    public DateTime CreatedAt { get; set; }
    public DateTime LastAt { get; set; }
}
```

- [ ] **Step 3: `UserEntity` 加 `CreatedAt`（保留旧 blob 列不动）**

```csharp
namespace ManInBlack.AI.Persistence.Entities;

public sealed class UserEntity
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";       // Finalize 阶段删除
    public string SessionIdsJson { get; set; } = "[]";     // Finalize 阶段删除
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 4: `ManInBlackDbContext` 加 `Sessions` DbSet + 映射**（FK 用字符串 `SessionId`，暂不加到 SessionMessages/Snapshots 的 FK——留给 Finalize）

在 `Users => Set<UserEntity>();` 下方加：

```csharp
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
```

在 `OnModelCreating` 的 `Users` 配置块**之后**追加：

```csharp
        modelBuilder.Entity<SessionEntity>(b =>
        {
            b.ToTable("Sessions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.SessionId).IsRequired();
            b.HasIndex(x => x.SessionId).IsUnique();
            b.Property(x => x.Source).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.LastAt).IsRequired();
            b.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
```

- [ ] **Step 5: 生成 Prep migration**

```bash
dotnet ef migrations add NormalizeSessionsPrep --project src/ManInBlack.AI --output-dir Persistence/Migrations
```

打开生成的 `<ts>_NormalizeSessionsPrep.cs`，确认 `Up` 包含：建 `Sessions` 表（含 `IX_Sessions_SessionId` unique）、给 `Users` 加 `CreatedAt` 列。**不应**出现对 `SessionMessages`/`AgentStateSnapshots` 的改动（时间类型切换放 Task 3）。若 EF 误加，删掉对应行。

- [ ] **Step 6: 编译 + 跑现有测试确认未破坏**

```bash
dotnet build src/ManInBlack.AI
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~SqliteUserStorageTests|FullyQualifiedName~DbContextSmokeTests"
```
Expected: 全绿（additive 改动不影响现有行为）。

- [ ] **Step 7: Commit**

```bash
git add src test docs
git commit -m "✨ [Persistence] Sessions 表 + SessionSource 枚举（additive, NormalizeSessionsPrep）"
```

---

## Task 2: 接口瘦身 + SqliteUserStorage 写 Sessions + 消费方适配

**目标：** `UserEntry` 去 Metadata/SessionIds、`IUserStorage` 新签名（`CreateNewSessionIdAsync` 写 `Sessions`、`GetLatestSessionIdAsync` 查表）；同步 SqliteUserStorage、FakeUserStorage、AgentFactory、BuiltinCommands。一次编译绿。

**Files:**
- Modify: `ISessionStorage.cs`（UserEntry 瘦身 + 删扩展）
- Modify: `IUserStorage.cs`
- Modify: `SqliteUserStorage.cs`
- Modify: `test/ManInBlack.AI.Tests/Helpers/FakeStorage.cs`
- Modify: `src/ManInBlack.AI/AgentFactory.cs`
- Modify: `src/ManInBlack.AI/Commands/BuiltinCommands.cs`

- [ ] **Step 1: 写失败测试 `SessionStorageNormalizationTests.cs`**

```csharp
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class SessionStorageNormalizationTests
{
    private static SqliteUserStorage CreateStorage(IDbContextFactory<ManInBlackDbContext> f) =>
        new(f, NullLogger<SqliteUserStorage>.Instance);

    [Fact]
    public async Task CreateNewSessionId_WritesSessionRow_InteractiveByDefault()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var sid = await storage.CreateNewSessionIdAsync("ext-1");

            await using var db = factory.CreateDbContext();
            var row = await db.Sessions.SingleAsync(x => x.SessionId == sid);
            Assert.Equal((int)SessionSource.Interactive, row.Source);
            Assert.StartsWith("ext-1_", row.SessionId);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task GetLatestSessionId_ReturnsLatestInteractive_ExcludesWebhook()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var oldInteractive = await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Interactive);
            await Task.Delay(1100); // 让 Unix 秒递增，保证 LastAt 不同
            var webhook = await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Webhook);
            await Task.Delay(1100);
            var newInteractive = await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Interactive);

            var latest = await storage.GetLatestSessionIdAsync("ext-1", SessionSource.Interactive);

            Assert.Equal(newInteractive, latest);            // 不是更新的 webhook
            Assert.NotEqual(webhook, latest);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task GetLatestSessionId_ReturnsNull_WhenUserHasNoSessionOfSource()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            await storage.CreateNewSessionIdAsync("ext-1", SessionSource.Webhook);
            var latest = await storage.GetLatestSessionIdAsync("ext-1", SessionSource.Interactive);
            Assert.Null(latest);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
}
```

- [ ] **Step 2: 跑测试确认失败（接口/方法不存在）**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~SessionStorageNormalizationTests"
```
Expected: 编译失败（`CreateNewSessionIdAsync` 无 source 重载、无 `GetLatestSessionIdAsync`）。

- [ ] **Step 3: 改 `ISessionStorage.cs` 的 `UserEntry` + 删扩展**

把 `UserEntry` 改为（删 `Metadata`、`SessionIds`，加 `CreatedAt`），并**删除** `UserEntryExtensions` 整个类：

```csharp
public record UserEntry
{
    public string UserId { get; set; } = "";
    public string SelfHostUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

（保留文件里的 `WorkspaceMode`/`WorkspaceSettings`/`AgentStorageOptions`/`ISessionStorage`/`IAgentStateStorage`/`ICheckpointPolicy` 不动。）

- [ ] **Step 4: 改 `IUserStorage.cs`**

```csharp
namespace ManInBlack.AI.Abstraction.Storage;

public interface IUserStorage
{
    Task<UserEntry> GetOrCreateUser(string userId);

    Task SaveUserAsync(UserEntry userEntry);

    /// <summary>为用户创建新会话并写入 Sessions 表，返回 SessionId。</summary>
    Task<string> CreateNewSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive);

    /// <summary>返回指定来源的最新会话 Id（按 LastAt 倒序），无则 null。</summary>
    Task<string?> GetLatestSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive);
}
```

- [ ] **Step 5: 重写 `SqliteUserStorage.cs`**

```csharp
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

[ServiceRegister.Singleton.As<IUserStorage>]
public class SqliteUserStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteUserStorage> logger) : IUserStorage
{
    public async Task<UserEntry> GetOrCreateUser(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (entity is not null) return ToEntry(entity);

        entity = new UserEntity { UserId = userId };
        db.Users.Add(entity);
        await db.SaveChangesAsync();
        logger.LogInformation("创建用户 {UserId} (SelfHostUserId={SelfHostUserId})", userId, entity.Id);
        return ToEntry(entity);
    }

    public async Task SaveUserAsync(UserEntry userEntry)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.Users.FirstOrDefaultAsync(x => x.UserId == userEntry.UserId)
            ?? throw new InvalidOperationException($"用户不存在: {userEntry.UserId}");
        // MetadataJson/SessionIdsJson 列在 Finalize 前仍存在；正规化后不再写入，保留旧值供数据搬迁读取。
        await db.SaveChangesAsync();
    }

    public async Task<string> CreateNewSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        var user = await GetOrCreateUser(userId);
        var sessionId = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        db.Sessions.Add(new SessionEntity
        {
            SessionId = sessionId,
            UserId = long.Parse(user.SelfHostUserId),
            Source = (int)source,
            CreatedAt = now,
            LastAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    public async Task<string?> GetLatestSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        await using var db = dbFactory.CreateDbContext();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (user is null) return null;
        var row = await db.Sessions.AsNoTracking()
            .Where(x => x.UserId == user.Id && x.Source == (int)source)
            .OrderByDescending(x => x.LastAt)
            .FirstOrDefaultAsync();
        return row?.SessionId;
    }

    private static UserEntry ToEntry(UserEntity e) => new()
    {
        UserId = e.UserId,
        SelfHostUserId = e.Id.ToString(),
        CreatedAt = e.CreatedAt,
    };
}
```

- [ ] **Step 6: 改 `FakeStorage.cs` 的 `FakeUserStorage`**

```csharp
public class FakeUserStorage : IUserStorage
{
    private readonly Dictionary<string, UserEntry> _users = new();
    private readonly Dictionary<string, List<(string SessionId, SessionSource Source, DateTime LastAt)>> _sessions = new();

    public Task<UserEntry> GetOrCreateUser(string userId)
    {
        if (!_users.TryGetValue(userId, out var user))
        {
            user = new UserEntry { UserId = userId };
            _users[userId] = user;
        }
        return Task.FromResult(user);
    }

    public Task SaveUserAsync(UserEntry userEntry)
    {
        _users[userEntry.UserId] = userEntry;
        return Task.CompletedTask;
    }

    public Task<string> CreateNewSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        var user = GetOrCreateUser(userId).GetAwaiter().GetResult();
        var sid = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        if (!_sessions.ContainsKey(userId)) _sessions[userId] = new();
        _sessions[userId].Add((sid, source, DateTime.UtcNow));
        return Task.FromResult(sid);
    }

    public Task<string?> GetLatestSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        if (!_sessions.TryGetValue(userId, out var list))
            return Task.FromResult<string?>(null);
        var latest = list.Where(x => x.Source == source).OrderByDescending(x => x.LastAt).FirstOrDefault();
        return Task.FromResult(latest.SessionId);
    }
}
```

- [ ] **Step 7: 改 `AgentFactory.cs:179`**

```csharp
            agentContext.SessionId = await userStorage.GetLatestSessionIdAsync(rootUserId, SessionSource.Interactive)
                                   ?? await userStorage.CreateNewSessionIdAsync(rootUserId, SessionSource.Interactive);
```
（确保文件顶部已 `using ManInBlack.AI.Abstraction.Storage;`。）

- [ ] **Step 8: 改 `BuiltinCommands.cs:22`**

```csharp
        context.SessionId = await userStorage.CreateNewSessionIdAsync(context.ParentId, SessionSource.Interactive);
```

- [ ] **Step 9: 跑测试确认通过**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~SessionStorageNormalizationTests"
```
Expected: 3 个测试全绿。

- [ ] **Step 10: 修 `SqliteUserStorageTests.cs`（旧测试引用了已删的 Metadata/SessionIds）**

删除 `SaveUser_PersistsMetadataAndSessionIds` 测试；改 `CreateNewSessionId_AppendsAndPersists` 为断言 `Sessions` 表有行：

```csharp
    [Fact]
    public async Task CreateNewSessionId_WritesSessionRow()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var sid = await storage.CreateNewSessionIdAsync("ext-1");
            await using var db = factory.CreateDbContext();
            Assert.True(await db.Sessions.AnyAsync(x => x.SessionId == sid));
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
```

- [ ] **Step 11: 全量编译 + 测试**

```bash
dotnet build
dotnet test test/ManInBlack.AI.Tests
```
Expected: 全绿（如有其它测试引用 `UserEntry.Metadata`/`.SessionIds`，一并修正为不依赖）。

- [ ] **Step 12: Commit**

```bash
git add src test
git commit -m "♻️ [Persistence] UserEntry 瘦身 + IUserStorage 新签名 + SqliteUserStorage 写 Sessions"
```

---

## Task 3: SqliteAgentStateStorage — LastAt 更新 + 时间 DateTime

**目标：** `SaveMessage` 写消息后更新对应 `Sessions.LastAt`；`CreatedAt`/`SavedAt` 改 `DateTime`。

**Files:**
- Modify: `Entities/SessionMessageEntity.cs`、`Entities/AgentStateSnapshotEntity.cs`
- Modify: `Persistence/SqliteAgentStateStorage.cs`
- Generate: 追加到 Prep migration 的下一步（见 Step）

- [ ] **Step 1: 改 `SessionMessageEntity.cs`**

```csharp
public sealed class SessionMessageEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string PayloadJson { get; set; } = "";
}
```

- [ ] **Step 2: 改 `AgentStateSnapshotEntity.cs`**

```csharp
public sealed class AgentStateSnapshotEntity
{
    public string SessionId { get; set; } = "";
    public DateTime SavedAt { get; set; }
    public string PayloadJson { get; set; } = "";
}
```

- [ ] **Step 3: `DbContext` 的 `SessionMessages`/`AgentStateSnapshots` 块无需改映射**（EF 按 CLR 类型推断；SQLite 仍存 TEXT）。但加 FK 留到 Finalize。

- [ ] **Step 4: 改 `SqliteAgentStateStorage.cs`**

把 `CreatedAt = DateTimeOffset.UtcNow.ToString("O")` → `CreatedAt = DateTime.UtcNow`；`SaveMessage` 末尾在 `SaveChangesAsync` 前加更新 `Sessions.LastAt`（若会话行不存在则补建，兜底）：

```csharp
    public async Task SaveMessage(string sessionId, ChatMessage message)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        db.SessionMessages.Add(new SessionMessageEntity
        {
            SessionId = sessionId,
            CreatedAt = now,
            PayloadJson = JsonSerializer.Serialize(message, JsonOptions),
        });

        // 更新 LastAt；会话行不存在则补建（Source=Interactive 兜底，正常路径由 CreateNewSessionIdAsync 预建）
        var session = await db.Sessions.FirstOrDefaultAsync(x => x.SessionId == sessionId);
        if (session is null)
        {
            db.Sessions.Add(new SessionEntity { SessionId = sessionId, Source = (int)SessionSource.Interactive, CreatedAt = now, LastAt = now });
        }
        else
        {
            session.LastAt = now;
        }

        await db.SaveChangesAsync();
    }
```
（顶部加 `using ManInBlack.AI.Abstraction.Storage;` 以用 `SessionSource`。）

`SaveSnapshotAsync` 里 `var savedAt = (...).ToString("O")` → `DateTime savedAt = snapshot.SavedAt == default ? DateTimeOffset.UtcNow.UtcDateTime : snapshot.SavedAt.UtcDateTime;`，赋给 `SavedAt = savedAt`（去掉字符串）。

- [ ] **Step 5: 生成 migration 反映时间类型变化**

```bash
dotnet ef migrations add NormalizeSessionsTimeTypes --project src/ManInBlack.AI --output-dir Persistence/Migrations
```
打开生成的文件：SQLite 下 string→DateTime 通常只产生 column 注释/重命名级别变更或为空。若 EF 生成了 `DropColumn`+`AddColumn`（表重建），**保留**——SQLite 会通过临时表重建，ISO 字符串数据被 EF 解析为 DateTime 再写回，无损。确认无 FK 丢失即可。

- [ ] **Step 6: 写/改测试：`SaveMessage` 更新 `LastAt`**

在 `SqliteAgentStateStorageTests.cs` 加：

```csharp
    [Fact]
    public async Task SaveMessage_UpdatesSessionLastAt()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = new SqliteAgentStateStorage(factory, NullLogger<SqliteAgentStateStorage>.Instance);
            await storage.SaveMessage("sid-1", new ChatMessage(ChatRole.User, "hi"));
            await Task.Delay(50);
            await storage.SaveMessage("sid-1", new ChatMessage(ChatRole.User, "again"));

            await using var db = factory.CreateDbContext();
            var session = await db.Sessions.SingleAsync(x => x.SessionId == "sid-1");
            var times = await db.SessionMessages.Where(x => x.SessionId == "sid-1").Select(x => x.CreatedAt).ToListAsync();
            Assert.Equal(times.Max(), session.LastAt);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
```

- [ ] **Step 7: 跑测试**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~SqliteAgentStateStorageTests"
```
Expected: 全绿。

- [ ] **Step 8: Commit**

```bash
git add src test
git commit -m "♻️ [Persistence] SaveMessage 更新 Sessions.LastAt + 时间列 DateTime"
```

---

## Task 4: Dashboard ChatHistoryQueries + ReadModels 重构

**目标：** `ListSessionsAsync` 直查 `Sessions` 表（join Users）、删全表扫的 `BuildSessionToUserMapAsync`；`ListUsersAsync` 去 Metadata/SessionIds；ReadModels 同步。

**Files:**
- Modify: `demo/Dashboard/Data/ChatHistoryQueries.cs`
- Modify: `demo/Dashboard/Data/ReadModels.cs`
- Modify: `test/Dashboard.Tests/ChatHistoryQueriesTests.cs`

- [ ] **Step 1: 改 `ReadModels.cs`**

```csharp
public sealed record SessionSummary
{
    public required string SessionId { get; init; }
    public required int MessageCount { get; init; }
    public required string FirstAt { get; init; }
    public required string LastAt { get; init; }
    public string? UserId { get; init; }
    public required int Source { get; init; }     // SessionSource
}

public sealed record UserSummary
{
    public required string UserId { get; init; }
    public required string CreatedAt { get; init; }
}
```
（`MessageBlock`/`MessageView`/`SearchResult` 不动。）

- [ ] **Step 2: 写失败测试（Dashboard.Tests）：`ListSessionsAsync` 来自 Sessions 表、带 Source**

在 `ChatHistoryQueriesTests.cs` 加（用 `SqliteTestHelpers` 同款工厂 + 手插 `SessionEntity` + `SessionMessageEntity`）：

```csharp
    [Fact]
    public async Task ListSessions_ReturnsFromSessionsTable_WithSource()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                var user = new UserEntity { UserId = "u1" };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                db.Sessions.Add(new SessionEntity { SessionId = "u1_1", UserId = user.Id, Source = (int)SessionSource.Interactive, CreatedAt = DateTime.UtcNow, LastAt = DateTime.UtcNow });
                db.SessionMessages.Add(new SessionMessageEntity { SessionId = "u1_1", CreatedAt = DateTime.UtcNow, PayloadJson = "{}" });
                await db.SaveChangesAsync();
            }
            var q = new ChatHistoryQueries(factory, NullLogger<ChatHistoryQueries>.Instance);
            var sessions = await q.ListSessionsAsync();
            var s = Assert.Single(sessions);
            Assert.Equal("u1_1", s.SessionId);
            Assert.Equal("u1", s.UserId);
            Assert.Equal((int)SessionSource.Interactive, s.Source);
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
```
（加 `using ManInBlack.AI.Persistence.Entities;`、`using ManInBlack.AI.Abstraction.Storage;`、`using Microsoft.Extensions.Logging.Abstractions;`。）

- [ ] **Step 3: 跑确认失败**

```bash
dotnet test test/Dashboard.Tests --filter "FullyQualifiedName~ListSessions_ReturnsFromSessionsTable_WithSource"
```
Expected: FAIL（旧实现走 GroupBy + SessionIdsJson 反查）。

- [ ] **Step 4: 重写 `ChatHistoryQueries.cs` 的 `ListSessionsAsync` + `ListUsersAsync`，删 `BuildSessionToUserMapAsync`**

```csharp
    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Sessions.AsNoTracking()
            .Join(db.Users,
                s => s.UserId,
                u => u.Id,
                (s, u) => new SessionSummary
                {
                    SessionId = s.SessionId,
                    MessageCount = db.SessionMessages.Count(m => m.SessionId == s.SessionId),
                    FirstAt = db.SessionMessages.Where(m => m.SessionId == s.SessionId).Min(m => (DateTime?)m.CreatedAt).ToString() ?? "",
                    LastAt = s.LastAt.ToString("O"),
                    UserId = u.UserId,
                    Source = s.Source,
                })
            .OrderByDescending(s => s.LastAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Users.AsNoTracking()
            .Select(u => new UserSummary { UserId = u.UserId, CreatedAt = u.CreatedAt.ToString("O") })
            .ToListAsync(ct);
    }
```

**删除**整个 `BuildSessionToUserMapAsync` 方法（含其对 `SessionIdsJson` 的反序列化）。`GetSessionMessagesAsync`/`SearchAsync` 不变。

- [ ] **Step 5: 跑测试**

```bash
dotnet test test/Dashboard.Tests
```
Expected: 全绿（修正其它引用 `UserSummary.Metadata`/`SessionIds` 的旧测试）。

- [ ] **Step 6: Commit**

```bash
git add demo test
git commit -m "♻️ [Dashboard] ListSessions 直查 Sessions 表，删全表扫映射 + 去 Metadata"
```

---

## Task 5: 启动期数据搬迁（blob → Sessions），分阶段 migrate

**目标：** 在 `MigrateManInBlackStorageAsync` 内，先 migrate 到 `NormalizeSessionsTimeTypes`，再跑幂等 C# 搬迁（读 `SessionIdsJson` → 写 `Sessions`，含孤儿补建），最后 migrate 到最新（Finalize 删 blob）。搬迁用 ADO.NET 读 blob（因搬迁后实体无此属性）。

**Files:**
- Create: `src/ManInBlack.AI/Persistence/NormalizeSessionsDataMigration.cs`
- Modify: `src/ManInBlack.AI/Persistence/StorageMigrationExtensions.cs`
- Create: `test/ManInBlack.AI.Tests/Persistence/NormalizeSessionsDataMigrationTests.cs`

- [ ] **Step 1: 写失败测试 `NormalizeSessionsDataMigrationTests.cs`**

```csharp
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class NormalizeSessionsDataMigrationTests
{
    [Fact]
    public async Task Migrate_MovesBlobToSessionsRows_AndHandlesOrphans()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            // 造旧数据：一个用户带 SessionIdsJson blob（含 1 个真会话），另有一条孤儿 SessionMessage
            await using (var db = factory.CreateDbContext())
            {
                db.Users.Add(new Persistence.Entities.UserEntity
                {
                    UserId = "u1",
                    SessionIdsJson = """["u1_1700000000"]""",
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
                db.SessionMessages.Add(new Persistence.Entities.SessionMessageEntity { SessionId = "u1_1700000000", CreatedAt = DateTime.UtcNow, PayloadJson = "{}" });
                db.SessionMessages.Add(new Persistence.Entities.SessionMessageEntity { SessionId = "orphan_9999", CreatedAt = DateTime.UtcNow, PayloadJson = "{}" }); // 孤儿
                await db.SaveChangesAsync();
            }

            await NormalizeSessionsDataMigration.RunAsync(factory);

            await using var db2 = factory.CreateDbContext();
            var rows = await db2.Sessions.ToDictionaryAsync(x => x.SessionId);
            Assert.True(rows.ContainsKey("u1_1700000000"));      // 来自 blob
            Assert.True(rows.ContainsKey("orphan_9999"));         // 孤儿补建
            Assert.All(rows.Values, r => Assert.Equal(0, r.Source)); // Interactive
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task Migrate_IsIdempotent()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                db.Users.Add(new Persistence.Entities.UserEntity { UserId = "u1", SessionIdsJson = """["u1_1"]""", CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            await NormalizeSessionsDataMigration.RunAsync(factory);
            await NormalizeSessionsDataMigration.RunAsync(factory);   // 第二次不应报错/重复
            await using var db = factory.CreateDbContext();
            Assert.Single(await db.Sessions.ToListAsync());
        }
        finally { sp.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
}
```

- [ ] **Step 2: 跑确认失败（`NormalizeSessionsDataMigration` 不存在）**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~NormalizeSessionsDataMigrationTests"
```
Expected: 编译失败。

- [ ] **Step 3: 实现 `NormalizeSessionsDataMigration.cs`**

```csharp
using System.Text.Json;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// 正规化数据搬迁：把旧 Users.SessionIdsJson blob 拆成 Sessions 行，并为孤儿 SessionMessages 补建 Sessions 行。
/// 在 Prep/TimeTypes migration 之后、Finalize（删 blob）之前执行。幂等：按 SessionId 存在性跳过。
/// 用 ADO.NET 读 blob（实体已无该属性）。
/// </summary>
public static class NormalizeSessionsDataMigration
{
    private record BlobUser(long Id, string UserId, string SessionIdsJson);

    public static async Task RunAsync(IDbContextFactory<ManInBlackDbContext> dbFactory, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();

        // 已有 Sessions（避免与新建并发重复）
        var existing = (await db.Sessions.AsNoTracking().Select(x => x.SessionId).ToListAsync(ct))
            .ToHashSet();

        // 1) 读 Users.SessionIdsJson（ADO.NET，列在 Finalize 前仍存在）
        var blobUsers = new List<BlobUser>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, UserId, SessionIdsJson FROM Users";
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                blobUsers.Add(new BlobUser(
                    rdr.GetInt64(0),
                    rdr.GetString(1),
                    rdr.IsDBNull(2) ? "[]" : rdr.GetString(2)));
            }
        }
        finally { await conn.CloseAsync(); }

        var now = DateTime.UtcNow;
        foreach (var bu in blobUsers)
        {
            List<string>? sids = null;
            try { sids = JsonSerializer.Deserialize<List<string>>(bu.SessionIdsJson); }
            catch (JsonException) { /* 坏 blob 跳过 */ }
            if (sids is null) continue;

            foreach (var sid in sids)
            {
                if (string.IsNullOrEmpty(sid) || !existing.Add(sid)) continue;
                var lastAt = await db.SessionMessages.AsNoTracking()
                    .Where(m => m.SessionId == sid).MaxAsync(m => (DateTime?)m.CreatedAt, ct) ?? now;
                db.Sessions.Add(new SessionEntity
                {
                    SessionId = sid,
                    UserId = bu.Id,
                    Source = (int)SessionSource.Interactive,
                    CreatedAt = ParseCreatedAt(sid, now),
                    LastAt = lastAt,
                });
            }
        }

        // 2) 孤儿：SessionMessages 引用但不在任何 blob 里的 sessionId
        var referenced = await db.SessionMessages.AsNoTracking().Select(m => m.SessionId).Distinct().ToListAsync(ct);
        foreach (var sid in referenced)
        {
            if (!existing.Add(sid)) continue;
            var lastAt = await db.SessionMessages.AsNoTracking()
                .Where(m => m.SessionId == sid).MaxAsync(m => (DateTime?)m.CreatedAt, ct) ?? now;
            db.Sessions.Add(new SessionEntity
            {
                SessionId = sid,
                UserId = 0,                                  // 未知归属用户
                Source = (int)SessionSource.Interactive,
                CreatedAt = lastAt,
                LastAt = lastAt,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static DateTime ParseCreatedAt(string sessionId, DateTime fallback)
    {
        var i = sessionId.LastIndexOf('_');
        if (i >= 0 && long.TryParse(sessionId[(i + 1)..], out var secs))
            return DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
        return fallback;
    }
}
```
> 注：`UserId = 0` 的孤儿行会在 Finalize 加 FK 后违反外键。Step 见 Task 7 处理（孤儿 FK：Finalize 前需保证每个孤儿补建时找到一个真实用户，或保留 `UserId` 指向一个占位用户；本 plan 在 Task 7 的 Step 中先把孤儿 `UserId` 修正为「该 sessionId 前缀对应的真实用户」，见 Task 7 Step 1）。

- [ ] **Step 4: 改 `StorageMigrationExtensions.cs` 为分阶段**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Persistence;

public static class StorageMigrationExtensions
{
    private const string LastPreFinalizeMigration = "NormalizeSessionsTimeTypes";

    public static async Task MigrateManInBlackStorageAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<ManInBlackDbContext>>();

        // 是否处于「blob 还在、Finalize 未应用」的窗口 → 需先跑到 TimeTypes、搬迁、再跑到最新
        bool needDataMigration;
        await using (var probe = factory.CreateDbContext())
        {
            var applied = await probe.Database.GetAppliedMigrationsAsync(ct);
            needDataMigration = applied.Contains(LastPreFinalizeMigration)
                && applied.Contains("NormalizeSessionsPrep")
                && !applied.Contains("NormalizeSessionsFinalize");
        }

        if (needDataMigration)
        {
            var migrator = (await using var db0 = factory.CreateDbContext(), db0.Database.GetService<IMigrator>());
            await migrator.MigrateAsync(LastPreFinalizeMigration, ct);
            await NormalizeSessionsDataMigration.RunAsync(factory, ct);
        }

        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }
}
```
> 说明：`needDataMigration` 仅在「Prep+TimeTypes 已应用、Finalize 未应用」时为真（即旧库首次升级到此版本的窗口）。全新库或已 Finalize 的库都为 false，不跑搬迁、不分阶段。这避免了向已 Finalize 库「降级到 TimeTypes」的风险。

- [ ] **Step 5: 跑搬迁测试**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~NormalizeSessionsDataMigrationTests"
```
Expected: 2 个测试绿（注：此阶段 Finalize 未生成，blob 列仍在，测试直接造旧数据）。

- [ ] **Step 6: Commit**

```bash
git add src test
git commit -m "♻️ [Persistence] 启动期 blob→Sessions 数据搬迁 + 分阶段 migrate"
```

---

## Task 6: JsonToSqliteMigrator 适配新 schema

**目标：** 一次性 JSON 导入也走正规化：写 `Sessions` 行（而非 SessionIdsJson）、去 Metadata、时间 `DateTime`。

**Files:**
- Modify: `src/ManInBlack.AI/Persistence/JsonToSqliteMigrator.cs`
- Modify: `test/ManInBlack.AI.Tests/Persistence/JsonToSqliteMigratorTests.cs`

- [ ] **Step 1: 改 `JsonToSqliteMigrator.MigrateAsync`**

要点（在原文件基础上）：
- 用户导入段：`db.Users.Add(new UserEntity { Id = internalIdNum, UserId = oriId, CreatedAt = DateTime.UtcNow })`（去掉 `MetadataJson`/`SessionIdsJson` 赋值——列在 Finalize 前仍在，不写即留默认；正规化后不依赖）。
- 读到 `entry.SessionIds`（来自旧 JSON 文件，反序列化仍可用——`UserEntry` 已瘦身无 SessionIds，故改用一个局部 `JsonElement` 解析旧文件：`var entry = JsonDocument.Parse(...)`；取 `SessionIds` 数组）。对每个 `sessionId`，导入一条 `SessionEntity { SessionId, UserId=internalIdNum, Source=Interactive, CreatedAt=ParseCreatedAt(sid, now), LastAt=该会话首条消息时间或 now }`。
- 会话历史段：`CreatedAt = DateTime.UtcNow`（去掉 `.ToString("O")`）。
- 快照段：`SavedAt = (s.SavedAt==default?DateTimeOffset.UtcNow:s.SavedAt).UtcDateTime`。

具体替换用户导入段（`foreach (var (oriId, internalId) in map)` 内）：

```csharp
                    db.Users.Add(new UserEntity
                    {
                        Id = internalIdNum,
                        UserId = oriId,
                        CreatedAt = DateTime.UtcNow,
                    });
                    // 旧文件里的 SessionIds → 写 Sessions 行
                    if (File.Exists(entryFile))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(entryFile, ct));
                            if (doc.RootElement.TryGetProperty("SessionIds", out var sidsEl) && sidsEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var sidEl in sidsEl.EnumerateArray())
                                {
                                    var sid = sidEl.GetString();
                                    if (string.IsNullOrEmpty(sid)) continue;
                                    if (await db.Sessions.AnyAsync(x => x.SessionId == sid, ct)) continue;
                                    db.Sessions.Add(new SessionEntity
                                    {
                                        SessionId = sid,
                                        UserId = internalIdNum,
                                        Source = (int)SessionSource.Interactive,
                                        CreatedAt = DateTime.UtcNow,
                                        LastAt = DateTime.UtcNow,
                                    });
                                }
                            }
                        }
                        catch (JsonException ex) { logger.LogWarning(ex, "迁移:用户 {Id} 条目损坏,用空值", oriId); }
                    }
                    usr++;
```
（顶部加 `using ManInBlack.AI.Abstraction.Storage;`；会话历史/快照段的 `CreatedAt`/`SavedAt` 改 `DateTime` 如上。）

- [ ] **Step 2: 跑 migrator 测试，按需修正断言**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~JsonToSqliteMigratorTests"
```
Expected: 绿；旧断言若检查 `SessionIdsJson` 列改为检查 `db.Sessions` 是否有对应行。

- [ ] **Step 3: Commit**

```bash
git add src test
git commit -m "♻️ [Persistence] JsonToSqliteMigrator 写 Sessions 行、去 Metadata、DateTime"
```

---

## Task 7: Finalize migration — FK + 删 blob 列；修正孤儿 FK

**目标：** 去掉 `UserEntity` 的 `MetadataJson`/`SessionIdsJson`；`SessionMessages`/`AgentStateSnapshots` 加 FK→`Sessions.SessionId`；生成 `NormalizeSessionsFinalize`。先修正孤儿 Sessions 行的 `UserId` 使其指向真实用户（避免 FK 失败）。

**Files:**
- Modify: `Entities/UserEntity.cs`、`ManInBlackDbContext.cs`
- Generate: `Migrations/<ts>_NormalizeSessionsFinalize.cs`

- [ ] **Step 1: 修正孤儿 `UserId`（在搬迁代码里）**

把 `NormalizeSessionsDataMigration.cs` 孤儿段的 `UserId = 0` 改为：按 sessionId 前缀（`{userId}_{ts}` 的 userId 部分）查真实用户：

```csharp
            // 解析 sessionId 前缀找归属用户
            long ownerId = 0;
            var ui = sid.LastIndexOf('_');
            if (ui > 0)
            {
                var prefix = sid[..ui];
                var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == prefix, ct);
                if (owner is not null) ownerId = owner.Id;
            }
            db.Sessions.Add(new SessionEntity
            {
                SessionId = sid,
                UserId = ownerId,
                Source = (int)SessionSource.Interactive,
                CreatedAt = lastAt,
                LastAt = lastAt,
            });
```
> 若 `ownerId == 0`（前缀对不上任何用户），FK 会失败——这类孤儿在 Finalize 加 FK 前需清理。在搬迁末尾加：`db.Sessions.RemoveRange(db.Sessions.Where(x => x.UserId == 0));`（删无主孤儿行；其 SessionMessages/Snapshots 由 FK `SetNull` 或级联——Finalize 里 FK 用 `ReferentialAction.NoAction`，故先删 Sessions 行不会动消息）。重跑对应搬迁测试确认无 `UserId=0` 残留。

- [ ] **Step 2: `UserEntity` 删 blob 列**

```csharp
public sealed class UserEntity
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 3: `DbContext` 加 SessionMessages/AgentStateSnapshots 的 FK**

在 `OnModelCreating` 对应块里加关系（`SessionId` → `Sessions.SessionId`）：

```csharp
        modelBuilder.Entity<SessionMessageEntity>(b =>
        {
            b.ToTable("SessionMessages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.SessionId).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.PayloadJson).IsRequired();
            b.HasIndex(x => new { x.SessionId, x.Id });
            b.HasOne<SessionEntity>()
                .WithMany()
                .HasForeignKey(x => x.SessionId)
                .HasPrincipalKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentStateSnapshotEntity>(b =>
        {
            b.ToTable("AgentStateSnapshots");
            b.HasKey(x => x.SessionId);
            b.Property(x => x.SavedAt).IsRequired();
            b.Property(x => x.PayloadJson).IsRequired();
            b.HasOne<SessionEntity>()
                .WithMany()
                .HasForeignKey(x => x.SessionId)
                .HasPrincipalKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
```
（`Sessions` 块的 `SessionId` 已有 `IsUnique()` 索引，可作为 FK principal key。）

- [ ] **Step 4: 生成 Finalize migration**

```bash
dotnet ef migrations add NormalizeSessionsFinalize --project src/ManInBlack.AI --output-dir Persistence/Migrations
```
打开生成文件确认 `Up`：加 `FK_SessionMessages_Sessions_SessionId`、`FK_AgentStateSnapshots_Sessions_SessionId`、删 `Users.MetadataJson`/`Users.SessionIdsJson`。EF 可能需要 `AddForeignKey`+`DropColumn`；确认顺序是先建 FK 再删 blob。

- [ ] **Step 5: 全量测试**

```bash
dotnet build
dotnet test
```
Expected: 全绿。重点跑 `NormalizeSessionsDataMigrationTests`（孤儿 UserId 修正）+ `Dashboard.Tests`。

- [ ] **Step 6: Commit**

```bash
git add src test
git commit -m "♻️ [Persistence] Finalize：加 FK + 删 blob 列（NormalizeSessionsFinalize）"
```

---

## Task 8: 文档 + 全量验证

- [ ] **Step 1: 更新 `docs/storage-guide.md`** 的 schema 描述：4 张表（Users/Sessions/SessionMessages/AgentStateSnapshots）、`Sessions.Source`、FK、`SessionSource` 枚举、启动期分阶段 migrate + 数据搬迁说明。

- [ ] **Step 2: 全量构建 + 全量测试**

```bash
dotnet build
dotnet test
```
Expected: 全绿。

- [ ] **Step 3: 旧库升级演练（手动）**

用一个含旧数据的开发库（或复制生产库结构）启动应用，确认：
1. 日志显示搬迁跑过（`needDataMigration` 窗口）。
2. `Sessions` 表行数 = 旧 `SessionIdsJson` 总和 + 孤儿。
3. 飞书入口正常起 agent（`GetLatestSessionIdAsync` 返回交互 latest）。
4. Dashboard 会话列表正常（来自 `Sessions`）。

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "📝 [docs] storage-guide 更新正规化 schema 与分阶段迁移"
```

---

## Self-Review（已执行）

**Spec coverage：**
- §目标 schema（4 表 + FK + DateTime + Source）→ Task 1/2/3/7。
- §组件改动 1-9（Entities/DbContext/接口/AgentFactory/BuiltinCommands/SqliteUserStorage/LastAt/Dashboard/JsonMigrator）→ Task 1-7 全覆盖。
- §数据迁移（两阶段 + C# 搬迁 + 幂等 + 孤儿）→ Task 5/7。
- §测试策略 → 各 Task 内 TDD。
- §范围/YAGNI（不拆 PayloadJson、不动管道）→ 未触及，符合。
- §后续 webhook → 不在本 plan（独立特性）。

**Placeholder scan：** 无 TBD/TODO；migration 文件由 `dotnet ef migrations add` 生成（EF 工作流，非占位）；每个代码步骤含完整代码。

**Type consistency：** `SessionSource.Interactive/Webhook`、`GetLatestSessionIdAsync(userId, source)`、`CreateNewSessionIdAsync(userId, source)`、`SessionEntity{SessionId,UserId,Source,CreatedAt,LastAt}`、`UserEntry{UserId,SelfHostUserId,CreatedAt}`、`SessionSummary.Source`、`UserSummary{UserId,CreatedAt}` 跨任务一致。`LastPreFinalizeMigration="NormalizeSessionsTimeTypes"` 与 Task 3 生成的 migration 名一致。
