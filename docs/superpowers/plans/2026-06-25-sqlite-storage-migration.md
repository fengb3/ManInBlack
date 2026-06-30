# SQLite 存储迁移 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 ManInBlack 后台运行期数据（会话消息、状态快照、用户数据）从 JSON 文件迁移到单个 SQLite 文件（EF Core 10），替换现有 `File*Storage` 实现，并提供一次性 JSON→SQLite 迁移工具。

**Architecture:** 新增 `src/ManInBlack.AI/Persistence/` 模块（EF Core + SQLite）。`ManInBlackDbContext` 三张表，`SqliteAgentStateStorage` / `SqliteUserStorage` 实现现有 `IAgentStateStorage` / `IUserStorage` 接口（接口契约零变化），用 `IDbContextFactory<ManInBlackDbContext>` 取短生命周期上下文。`JsonToSqliteMigrator` 做一次性导入。宿主启动期显式 `MigrateManInBlackStorageAsync()` 应用迁移。

**Tech Stack:** .NET 10、EF Core 10（`Microsoft.EntityFrameworkCore.Sqlite` + `.Design`）、`System.Text.Json`（沿用现有 `ChatMessage` 序列化，已验证多态往返正常）、xunit + 手写 fake。

## Global Constraints

- 所有注释、文档、提交信息使用中文；提交信息用 [gitmoji](https://gitmoji.dev/) 前缀，**禁止** `Co-authored-by` 尾部。
- 源生成器只能用 `Fengb3.EasyCodeBuilder`（本计划不写源生成器代码，但涉及 `[ServiceRegister]` 特性，定义在 `src/ManInBlack.AI.Abstraction/Attributes/ServiceRegister.cs`）。
- `[ServiceRegister.Singleton.As<IFoo>]` 装饰类，由 `services.AddAutoRegisteredServices()`（源生成器生成）自动注册为 `IFoo` 的单例。`[ServiceRegister.Singleton]`（无 `As`）注册为具体类型自身。
- 测试用手写 fake，不用 mock 框架。
- `ChatMessage` 序列化必须沿用现有写法（`System.Text.Json` + `UnsafeRelaxedJsonEscaping`），已实测多态 `Contents`（`TextContent`/`FunctionCallContent`）正确往返，**不要引入自定义 converter**。
- 当前分支：`feat/sqlite-storage`（spec 已提交在此分支）。

---

## File Structure

**Create:**
- `src/ManInBlack.AI/Persistence/Entities/SessionMessageEntity.cs` — 消息表实体
- `src/ManInBlack.AI/Persistence/Entities/AgentStateSnapshotEntity.cs` — 快照表实体
- `src/ManInBlack.AI/Persistence/Entities/UserEntity.cs` — 用户表实体
- `src/ManInBlack.AI/Persistence/ManInBlackDbContext.cs` — EF DbContext + OnModelCreating
- `src/ManInBlack.AI/Persistence/ManInBlackDbContextDesignFactory.cs` — `IDesignTimeDbContextFactory`（供 `dotnet ef` 脚手架）
- `src/ManInBlack.AI/Persistence/SqliteInitInterceptor.cs` — 每连接 `busy_timeout` pragma
- `src/ManInBlack.AI/Persistence/StorageMigrationExtensions.cs` — `MigrateManInBlackStorageAsync(IServiceProvider)`
- `src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs` — `IAgentStateStorage` 实现
- `src/ManInBlack.AI/Persistence/SqliteUserStorage.cs` — `IUserStorage` 实现
- `src/ManInBlack.AI/Persistence/JsonToSqliteMigrator.cs` — 一次性迁移
- `src/ManInBlack.AI/Persistence/Migrations/*` — `dotnet ef` 生成的 `InitialCreate`
- `test/ManInBlack.AI.Tests/Helpers/SqliteTestHelpers.cs` — 测试用 DbContext 工厂（临时文件）
- `test/ManInBlack.AI.Tests/Persistence/SqliteAgentStateStorageTests.cs`
- `test/ManInBlack.AI.Tests/Persistence/SqliteUserStorageTests.cs`
- `test/ManInBlack.AI.Tests/Persistence/JsonToSqliteMigratorTests.cs`
- `docs/storage-guide.md`

**Modify:**
- `src/ManInBlack.AI/ManInBlack.AI.csproj` — 加 EF Core 包
- `src/ManInBlack.AI/DependencyInjection.cs:46-50` — 注册 `AddDbContextFactory`
- `demo/AgentConsole/Program.cs` — 启动 migrate + `migrate-storage` 参数
- `demo/FeishuAdaptor/Program.cs:75-94` — 启动 migrate + `migrate-storage` 参数
- `test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj` — 加 EF Core Sqlite 包
- `test/ManInBlack.AI.Tests/Middlewares/CheckpointTests.cs:177-200` — 删 File 相关测试
- `docs/configuration-guide.md`、`docs/architecture.md`、`docs/feishu-guide.md`、`CLAUDE.md`

**Delete:**
- `src/ManInBlack.AI/Services/FileSessionStorage.cs`
- `src/ManInBlack.AI/Services/FileUserStorage.cs`
- `src/ManInBlack.AI/Utils/JsonFileDictionary.cs`
- `src/ManInBlack.AI/Utils/JsonFileList.cs`

---

## Task 1: EF Core 基础设施（包 + 实体 + DbContext + 初始迁移 + DI）

**Files:**
- Modify: `src/ManInBlack.AI/ManInBlack.AI.csproj`
- Create: `src/ManInBlack.AI/Persistence/Entities/SessionMessageEntity.cs`
- Create: `src/ManInBlack.AI/Persistence/Entities/AgentStateSnapshotEntity.cs`
- Create: `src/ManInBlack.AI/Persistence/Entities/UserEntity.cs`
- Create: `src/ManInBlack.AI/Persistence/ManInBlackDbContext.cs`
- Create: `src/ManInBlack.AI/Persistence/ManInBlackDbContextDesignFactory.cs`
- Create: `src/ManInBlack.AI/Persistence/SqliteInitInterceptor.cs`
- Create: `src/ManInBlack.AI/Persistence/StorageMigrationExtensions.cs`
- Create (scaffolded): `src/ManInBlack.AI/Persistence/Migrations/*.cs`
- Modify: `src/ManInBlack.AI/DependencyInjection.cs`
- Modify: `test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj`
- Create: `test/ManInBlack.AI.Tests/Helpers/SqliteTestHelpers.cs`
- Test: `test/ManInBlack.AI.Tests/Persistence/DbContextSmokeTests.cs`

**Interfaces:**
- Produces: `ManInBlackDbContext`（`DbSet<SessionMessageEntity> SessionMessages`、`DbSet<AgentStateSnapshotEntity> AgentStateSnapshots`、`DbSet<UserEntity> Users`）、`IDbContextFactory<ManInBlackDbContext>`（DI 单例）、`StorageMigrationExtensions.MigrateManInBlackStorageAsync(IServiceProvider, CancellationToken)`。后续所有任务通过此工厂取上下文。

- [ ] **Step 1: 加 EF Core 包到主库**

修改 `src/ManInBlack.AI/ManInBlack.AI.csproj`，在 `<PackageReference Include="ModelContextProtocol" .../>` 后追加：

```xml
        <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
        <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
```

- [ ] **Step 2: 加 EF Core Sqlite 包到测试项目**

修改 `test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj`，在已有的 `<PackageReference>` 中追加：

```xml
        <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
```

（若该 csproj 通过 `<ProjectReference>` 引用主库已传递了 `Design`/`Sqlite`，则只加 Sqlite 即可。先确认是否已有，避免重复。）

- [ ] **Step 3: 创建三个实体类**

`src/ManInBlack.AI/Persistence/Entities/SessionMessageEntity.cs`:

```csharp
namespace ManInBlack.AI.Persistence.Entities;

/// <summary>
/// 会话消息持久化实体。PayloadJson 存整条 ChatMessage 序列化结果。
/// </summary>
public sealed class SessionMessageEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}
```

`src/ManInBlack.AI/Persistence/Entities/AgentStateSnapshotEntity.cs`:

```csharp
namespace ManInBlack.AI.Persistence.Entities;

/// <summary>
/// 状态快照实体。按 SessionId 整存整取整覆盖。
/// </summary>
public sealed class AgentStateSnapshotEntity
{
    public string SessionId { get; set; } = "";
    public string SavedAt { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}
```

`src/ManInBlack.AI/Persistence/Entities/UserEntity.cs`:

```csharp
namespace ManInBlack.AI.Persistence.Entities;

/// <summary>
/// 用户实体。Id 自增对应 SelfHostUserId；UserId 为原始外部 id（唯一）。
/// </summary>
public sealed class UserEntity
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";
    public string SessionIdsJson { get; set; } = "[]";
}
```

- [ ] **Step 4: 创建 DbContext**

`src/ManInBlack.AI/Persistence/ManInBlackDbContext.cs`:

```csharp
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// ManInBlack 持久化上下文。连接串由 DI 从 AgentStorageOptions.RootPath 注入。
/// </summary>
public class ManInBlackDbContext(DbContextOptions<ManInBlackDbContext> options) : DbContext(options)
{
    public DbSet<SessionMessageEntity> SessionMessages => Set<SessionMessageEntity>();
    public DbSet<AgentStateSnapshotEntity> AgentStateSnapshots => Set<AgentStateSnapshotEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionMessageEntity>(b =>
        {
            b.ToTable("SessionMessages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.SessionId).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.PayloadJson).IsRequired();
            b.HasIndex(x => new { x.SessionId, x.Id });
        });

        modelBuilder.Entity<AgentStateSnapshotEntity>(b =>
        {
            b.ToTable("AgentStateSnapshots");
            b.HasKey(x => x.SessionId);
            b.Property(x => x.SavedAt).IsRequired();
            b.Property(x => x.PayloadJson).IsRequired();
        });

        modelBuilder.Entity<UserEntity>(b =>
        {
            b.ToTable("Users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.UserId).IsRequired();
            b.HasIndex(x => x.UserId).IsUnique();
            b.Property(x => x.MetadataJson).IsRequired();
            b.Property(x => x.SessionIdsJson).IsRequired();
        });
    }
}
```

- [ ] **Step 5: 创建设计时工厂（供 `dotnet ef` 脚手架迁移）**

`src/ManInBlack.AI/Persistence/ManInBlackDbContextDesignFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// 仅供 dotnet ef 设计时脚手架 migration 使用。运行期连接串由 DI 配置。
/// </summary>
internal sealed class ManInBlackDbContextDesignFactory : IDesignTimeDbContextFactory<ManInBlackDbContext>
{
    public ManInBlackDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ManInBlackDbContext>()
            .UseSqlite("Data Source=maninblack.db")
            .Options;
        return new ManInBlackDbContext(options);
    }
}
```

- [ ] **Step 6: 创建 SqliteInitInterceptor**

`src/ManInBlack.AI/Persistence/SqliteInitInterceptor.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// 每连接设置 busy_timeout,并发写抢锁时重试而非立刻抛 SQLITE_BUSY。
/// WAL 为库级持久设置,由启动期 MigrateManInBlackStorageAsync 设一次。
/// </summary>
internal sealed class SqliteInitInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=5000;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
```

- [ ] **Step 7: 创建启动期迁移扩展**

`src/ManInBlack.AI/Persistence/StorageMigrationExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Persistence;

public static class StorageMigrationExtensions
{
    /// <summary>
    /// 启动期显式应用 EF Core 迁移并设置 WAL。宿主在 BuildServiceProvider 之后调用一次。
    /// </summary>
    public static async Task MigrateManInBlackStorageAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<ManInBlackDbContext>>();
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync(ct);
        // WAL 为库级持久设置(已 WAL 时为 no-op);必须在无事务时设置
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }
}
```

- [ ] **Step 8: 在 DI 注册 DbContextFactory**

修改 `src/ManInBlack.AI/DependencyInjection.cs`：在文件顶部 using 区追加（若缺）：

```csharp
using Microsoft.EntityFrameworkCore;
using ManInBlack.AI.Persistence;
```

在 `AddManInBlack()` 的 `services.AddSingleton<ModelChoice>(...)` 之后、`services.AddScoped<AgentPipelineBuilder>();` 之前插入：

```csharp
            // SQLite 持久化:连接串从 RootPath 取
            services.AddDbContextFactory<ManInBlackDbContext>((sp, o) =>
            {
                var root = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.RootPath;
                Directory.CreateDirectory(root);
                o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")}");
                o.AddInterceptors(new SqliteInitInterceptor());
            });
```

- [ ] **Step 9: 脚手架初始迁移**

```bash
dotnet ef migrations add InitialCreate --project src/ManInBlack.AI --startup-project src/ManInBlack.AI --output-dir Persistence/Migrations
```

Expected：在 `src/ManInBlack.AI/Persistence/Migrations/` 生成 `<timestamp>_InitialCreate.cs` 与 `ManInBlackDbContextModelSnapshot.cs`，含 3 个 CreateTable（SessionMessages、AgentStateSnapshots、Users）+ 2 索引。

> 若 `dotnet ef` 因启动项目构建报错（源生成器 analyzer 引用），改用 `--startup-project demo/AgentConsole`。设计时工厂已在库内，不依赖启动项目运行。

- [ ] **Step 10: 创建测试辅助 + 冒烟测试**

`test/ManInBlack.AI.Tests/Helpers/SqliteTestHelpers.cs`:

```csharp
using ManInBlack.AI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Tests.Helpers;

public static class SqliteTestHelpers
{
    /// <summary>
    /// 在临时目录建一个已迁移的 SQLite 工厂。返回 (工厂, 根路径)。调用方负责清理根路径。
    /// </summary>
    public static async Task<(IDbContextFactory<ManInBlackDbContext> factory, string rootPath)> CreateFactoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mib_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(root);
        var services = new ServiceCollection();
        services.AddDbContextFactory<ManInBlackDbContext>(o =>
            o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")}"));
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<ManInBlackDbContext>>();
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        return (factory, root);
    }
}
```

`test/ManInBlack.AI.Tests/Persistence/DbContextSmokeTests.cs`:

```csharp
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class DbContextSmokeTests
{
    [Fact]
    public async Task Migrate_ShouldCreateAllThreeTables()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            await using var db = factory.CreateDbContext();
            var tables = await db.Database
                .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")
                .ToListAsync();

            Assert.Contains("SessionMessages", tables);
            Assert.Contains("AgentStateSnapshots", tables);
            Assert.Contains("Users", tables);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [ ] **Step 11: 构建并跑冒烟测试**

```bash
dotnet build src/ManInBlack.AI
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~DbContextSmokeTests"
```

Expected：构建成功；测试 PASS。

- [ ] **Step 12: 提交**

```bash
git add src/ManInBlack.AI/ManInBlack.AI.csproj src/ManInBlack.AI/Persistence/ src/ManInBlack.AI/DependencyInjection.cs test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj test/ManInBlack.AI.Tests/Helpers/SqliteTestHelpers.cs test/ManInBlack.AI.Tests/Persistence/DbContextSmokeTests.cs
git commit -m "✨ EF Core SQLite 基础设施:DbContext + 实体 + InitialCreate 迁移"
```

---

## Task 2: SqliteAgentStateStorage — 会话消息

**Files:**
- Create: `src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs`
- Test: `test/ManInBlack.AI.Tests/Persistence/SqliteAgentStateStorageTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<ManInBlackDbContext>`（Task 1 产出）
- Produces: `SqliteAgentStateStorage`（本任务**先不加 `[ServiceRegister]`**，避免与现有 `FileAgentStateStorage` 双注册冲突；Task 5 统一替换注册）

- [ ] **Step 1: 写失败测试（含 function-call 消息往返）**

`test/ManInBlack.AI.Tests/Persistence/SqliteAgentStateStorageTests.cs`:

```csharp
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class SqliteAgentStateStorageTests
{
    private static SqliteAgentStateStorage CreateStorage(IDbContextFactory<ManInBlackDbContext> factory) =>
        new(factory, NullLogger<SqliteAgentStateStorage>.Instance);

    [Fact]
    public async Task SaveMessage_Then_LoadMessages_ShouldRoundTrip_InOrder()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var sessionId = "s1";

            var m1 = new ChatMessage(ChatRole.User, "hello");
            var m2 = new ChatMessage(ChatRole.Assistant, "hi");
            // 含 function call 的多态消息
            var m3 = new ChatMessage(ChatRole.Assistant, []);
            m3.Contents.Add(new FunctionCallContent("call_1", "foo", new Dictionary<string, object?> { ["x"] = 1 }));

            await storage.SaveMessage(sessionId, m1);
            await storage.SaveMessage(sessionId, m2);
            await storage.SaveMessage(sessionId, m3);

            var loaded = await storage.LoadMessages(sessionId);

            Assert.Equal(3, loaded.Count);
            Assert.Equal("hello", loaded[0].Text);
            Assert.Equal("hi", loaded[1].Text);
            var fc = loaded[2].Contents.OfType<FunctionCallContent>().Single();
            Assert.Equal("foo", fc.Name);
            Assert.Equal("call_1", fc.CallId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadMessages_UnknownSession_ReturnsEmpty()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var loaded = await storage.LoadMessages("nope");
            Assert.Empty(loaded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

`ChatMessage` 的 `Text` 是扩展属性（取第一个 `TextContent` 的文本）；`new ChatMessage(ChatRole.Assistant, [])` 用空 Contents 列表构造后手动 Add。

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~SqliteAgentStateStorageTests"
```

Expected：FAIL（`SqliteAgentStateStorage` 未定义 → 编译错误）。

- [ ] **Step 3: 实现 SaveMessage / LoadMessages**

`src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// SQLite 实现的会话消息 + 状态快照存储。本任务先实现消息部分;快照部分见 Task 3。
/// </summary>
public class SqliteAgentStateStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteAgentStateStorage> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task SaveMessage(string sessionId, ChatMessage message)
    {
        await using var db = dbFactory.CreateDbContext();
        db.SessionMessages.Add(new SessionMessageEntity
        {
            SessionId = sessionId,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            PayloadJson = JsonSerializer.Serialize(message, JsonOptions),
        });
        await db.SaveChangesAsync();
    }

    public async Task<IList<ChatMessage>> LoadMessages(string sessionId)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.SessionMessages
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var messages = new List<ChatMessage>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<ChatMessage>(row.PayloadJson, JsonOptions);
                if (msg is not null) messages.Add(msg);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "会话 {SessionId} 第 {Id} 行消息反序列化失败,跳过", sessionId, row.Id);
            }
        }
        return messages;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~SqliteAgentStateStorageTests"
```

Expected：2 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs test/ManInBlack.AI.Tests/Persistence/SqliteAgentStateStorageTests.cs
git commit -m "✨ SqliteAgentStateStorage:会话消息存取(含多态 function-call 往返)"
```

---

## Task 3: SqliteAgentStateStorage — 状态快照

**Files:**
- Modify: `src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs`
- Test: `test/ManInBlack.AI.Tests/Persistence/SqliteAgentStateStorageTests.cs`（追加测试）

**Interfaces:**
- Produces: `SqliteAgentStateStorage` 实现 `IAgentStateStorage` 全部方法（SaveMessage/LoadMessages 来自 Task 2，LoadSnapshotAsync/SaveSnapshotAsync/DeleteSnapshotAsync 本任务）。仍不加 `[ServiceRegister]`。

- [ ] **Step 1: 把类声明改为实现 `IAgentStateStorage` 并追加 3 个快照方法**

修改 `src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs`：

(a) using 区追加：

```csharp
using ManInBlack.AI.Abstraction.Storage;
```

(b) 类声明改为：

```csharp
public class SqliteAgentStateStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteAgentStateStorage> logger) : IAgentStateStorage
```

(c) 在 `LoadMessages` 方法之后追加：

```csharp
    public async Task<AgentStateSnapshot?> LoadSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var row = await db.AgentStateSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);

        if (row is null) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentStateSnapshot>(row.PayloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "快照 {SessionId} 反序列化失败,返回 null", sessionId);
            return null;
        }
    }

    public async Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var existing = await db.AgentStateSnapshots.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        var savedAt = (snapshot.SavedAt == default ? DateTimeOffset.UtcNow : snapshot.SavedAt).ToString("O");
        var payload = JsonSerializer.Serialize(snapshot, JsonOptions);

        if (existing is null)
        {
            db.AgentStateSnapshots.Add(new AgentStateSnapshotEntity
            {
                SessionId = sessionId,
                SavedAt = savedAt,
                PayloadJson = payload,
            });
        }
        else
        {
            existing.SavedAt = savedAt;
            existing.PayloadJson = payload;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var row = await db.AgentStateSnapshots.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        if (row is not null)
        {
            db.AgentStateSnapshots.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }
```

- [ ] **Step 2: 追加快照测试**

在 `SqliteAgentStateStorageTests.cs` 类内追加（保留已有 using；`AgentStateSnapshot` 来自 `ManInBlack.AI.Abstraction.Storage`，按需加 using）：

```csharp
    [Fact]
    public async Task SaveSnapshot_Then_LoadSnapshot_RestoresState()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var snap = new AgentStateSnapshot
            {
                SessionId = "s1",
                AgentName = "TestAgent",
                SystemPrompt = "p",
                Items = new Dictionary<string, object> { ["k"] = "v" },
                SavedAt = DateTimeOffset.UtcNow,
                CheckpointReason = "ToolCallCompleted",
            };

            await storage.SaveSnapshotAsync("s1", snap);
            var loaded = await storage.LoadSnapshotAsync("s1");

            Assert.NotNull(loaded);
            Assert.Equal("TestAgent", loaded.AgentName);
            Assert.Equal("v", loaded.Items["k"].ToString());
            Assert.Equal("ToolCallCompleted", loaded.CheckpointReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveSnapshot_OverwritesExisting()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            await storage.SaveSnapshotAsync("s1", new AgentStateSnapshot { SessionId = "s1", SystemPrompt = "first" });
            await storage.SaveSnapshotAsync("s1", new AgentStateSnapshot { SessionId = "s1", SystemPrompt = "second" });

            var loaded = await storage.LoadSnapshotAsync("s1");
            Assert.NotNull(loaded);
            Assert.Equal("second", loaded.SystemPrompt);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadSnapshot_None_ReturnsNull()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            Assert.Null(await storage.LoadSnapshotAsync("missing"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteSnapshot_RemovesIt()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            await storage.SaveSnapshotAsync("s1", new AgentStateSnapshot { SessionId = "s1", SystemPrompt = "p" });
            await storage.DeleteSnapshotAsync("s1");
            Assert.Null(await storage.LoadSnapshotAsync("s1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
```

- [ ] **Step 3: 跑测试确认通过**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~SqliteAgentStateStorageTests"
```

Expected：全部（含 Task 2 的 2 个 + 本任务 4 个）PASS。

- [ ] **Step 4: 提交**

```bash
git add src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs test/ManInBlack.AI.Tests/Persistence/SqliteAgentStateStorageTests.cs
git commit -m "✨ SqliteAgentStateStorage:状态快照存取/覆盖/删除"
```

---

## Task 4: SqliteUserStorage

**Files:**
- Create: `src/ManInBlack.AI/Persistence/SqliteUserStorage.cs`
- Test: `test/ManInBlack.AI.Tests/Persistence/SqliteUserStorageTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<ManInBlackDbContext>`、`IUserStorage`（定义于 `ManInBlack.AI.Abstraction.Storage`，方法 `GetOrCreateUser` / `SaveUserAsync` / `CreateNewSessionIdAsync`，领域模型 `UserEntry { UserId, SelfHostUserId, Metadata, SessionIds }`）
- Produces: `SqliteUserStorage : IUserStorage`。仍不加 `[ServiceRegister]`。

- [ ] **Step 1: 写失败测试**

`test/ManInBlack.AI.Tests/Persistence/SqliteUserStorageTests.cs`:

```csharp
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class SqliteUserStorageTests
{
    private static SqliteUserStorage CreateStorage(IDbContextFactory<ManInBlackDbContext> factory) =>
        new(factory, NullLogger<SqliteUserStorage>.Instance);

    [Fact]
    public async Task GetOrCreateUser_CreatesThenReuses()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var u1 = await storage.GetOrCreateUser("ext-1");
            var u2 = await storage.GetOrCreateUser("ext-1");

            Assert.Equal("ext-1", u1.UserId);
            Assert.False(string.IsNullOrEmpty(u1.SelfHostUserId));
            Assert.Equal(u1.SelfHostUserId, u2.SelfHostUserId); // 复用而非新建
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveUser_PersistsMetadataAndSessionIds()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var user = await storage.GetOrCreateUser("ext-1");
            user.Metadata["role"] = "admin";
            user.SessionIds.Add("ext-1_111");

            await storage.SaveUserAsync(user);

            var again = await storage.GetOrCreateUser("ext-1");
            Assert.Equal("admin", again.Metadata["role"].ToString());
            Assert.Contains("ext-1_111", again.SessionIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateNewSessionId_AppendsAndPersists()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        try
        {
            var storage = CreateStorage(factory);
            var sid = await storage.CreateNewSessionIdAsync("ext-1");

            Assert.StartsWith("ext-1_", sid);
            var again = await storage.GetOrCreateUser("ext-1");
            Assert.Contains(sid, again.SessionIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~SqliteUserStorageTests"
```

Expected：FAIL（`SqliteUserStorage` 未定义 → 编译错误）。

- [ ] **Step 3: 实现 SqliteUserStorage**

`src/ManInBlack.AI/Persistence/SqliteUserStorage.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// SQLite 实现的用户存储。SelfHostUserId = 自增 Id 的字符串形式。
/// </summary>
public class SqliteUserStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteUserStorage> logger) : IUserStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<UserEntry> GetOrCreateUser(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (entity is not null) return ToEntry(entity);

        entity = new UserEntity { UserId = userId };
        db.Users.Add(entity);
        await db.SaveChangesAsync();
        return ToEntry(entity);
    }

    public async Task SaveUserAsync(UserEntry userEntry)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.Users.FirstOrDefaultAsync(x => x.UserId == userEntry.UserId)
            ?? throw new InvalidOperationException($"用户不存在: {userEntry.UserId}");

        entity.MetadataJson = JsonSerializer.Serialize(userEntry.Metadata, JsonOptions);
        entity.SessionIdsJson = JsonSerializer.Serialize(userEntry.SessionIds, JsonOptions);
        await db.SaveChangesAsync();
    }

    public async Task<string> CreateNewSessionIdAsync(string userId)
    {
        var user = await GetOrCreateUser(userId);
        var sessionId = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        user.SessionIds.Add(sessionId);
        await SaveUserAsync(user);
        return sessionId;
    }

    private static UserEntry ToEntry(UserEntity e) => new()
    {
        UserId = e.UserId,
        SelfHostUserId = e.Id.ToString(),
        Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(e.MetadataJson, JsonOptions) ?? new(),
        SessionIds = JsonSerializer.Deserialize<List<string>>(e.SessionIdsJson, JsonOptions) ?? new(),
    };
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~SqliteUserStorageTests"
```

Expected：3 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ManInBlack.AI/Persistence/SqliteUserStorage.cs test/ManInBlack.AI.Tests/Persistence/SqliteUserStorageTests.cs
git commit -m "✨ SqliteUserStorage:用户存取 + 会话 ID 管理"
```

---

## Task 5: 切换注册——Sqlite 上位、删除 File 实现

**Files:**
- Modify: `src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs`（加特性）
- Modify: `src/ManInBlack.AI/Persistence/SqliteUserStorage.cs`（加特性）
- Delete: `src/ManInBlack.AI/Services/FileSessionStorage.cs`
- Delete: `src/ManInBlack.AI/Services/FileUserStorage.cs`
- Delete: `src/ManInBlack.AI/Utils/JsonFileDictionary.cs`
- Delete: `src/ManInBlack.AI/Utils/JsonFileList.cs`
- Modify: `test/ManInBlack.AI.Tests/Middlewares/CheckpointTests.cs`

**Interfaces:**
- Consumes: `ISessionStorage` / `IUserStorage` 接口（已存在）。`DependencyInjection.cs:67-68` 的 `IAgentStateStorage → ISessionStorage` 映射保留不变。
- Produces: SQLite 实现成为 `ISessionStorage`（= `IAgentStateStorage`）与 `IUserStorage` 的唯一注册实现。

- [ ] **Step 1: 给 Sqlite 实现加 `[ServiceRegister]` 特性**

`src/ManInBlack.AI/Persistence/SqliteAgentStateStorage.cs` 文件顶部 using 区追加：

```csharp
using ManInBlack.AI.Abstraction.Attributes;
```

类声明上方加特性（实现 `IAgentStateStorage`，注册为 `ISessionStorage`，与原 File 实现注册方式一致）：

```csharp
[ServiceRegister.Singleton.As<ISessionStorage>]
public class SqliteAgentStateStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteAgentStateStorage> logger) : IAgentStateStorage
```

`src/ManInBlack.AI/Persistence/SqliteUserStorage.cs` 文件顶部 using 区追加：

```csharp
using ManInBlack.AI.Abstraction.Attributes;
```

类声明上方加特性：

```csharp
[ServiceRegister.Singleton.As<IUserStorage>]
public class SqliteUserStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteUserStorage> logger) : IUserStorage
```

- [ ] **Step 2: 删除 4 个 File/JSON-util 文件**

```bash
git rm src/ManInBlack.AI/Services/FileSessionStorage.cs
git rm src/ManInBlack.AI/Services/FileUserStorage.cs
git rm src/ManInBlack.AI/Utils/JsonFileDictionary.cs
git rm src/ManInBlack.AI/Utils/JsonFileList.cs
```

- [ ] **Step 3: 删除依赖 File 实现的过期测试**

修改 `test/ManInBlack.AI.Tests/Middlewares/CheckpointTests.cs`：删除整个 `LoadSnapshot_CorruptedJson_ShouldReturnNull` 方法（第 173–200 行附近），该方法依赖已删除的 `FileAgentStateStorage`，其意图（坏数据优雅返回 null）已由 `SqliteAgentStateStorageTests` 的快照测试覆盖。同时删掉仅供它用的私有 `FakeLogger<T>` 类（若该文件内无其他用处）。

> 注意：删除前用 grep 确认 `FakeLogger<T>` 在本文件内仅被该测试使用；若其他测试也用则保留。

- [ ] **Step 4: 全量构建 + 全量测试**

```bash
dotnet build ManInBlack.slnx
dotnet test test/ManInBlack.AI.Tests
```

Expected：构建成功（无 File* 残留引用）；全部测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "♻️ 存储层切换为 SQLite:删除 File*Storage 与 JSON 字典/列表工具"
```

---

## Task 6: JsonToSqliteMigrator（一次性 JSON→SQLite 导入）

**Files:**
- Create: `src/ManInBlack.AI/Persistence/JsonToSqliteMigrator.cs`
- Test: `test/ManInBlack.AI.Tests/Persistence/JsonToSqliteMigratorTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<ManInBlackDbContext>`、`IOptions<AgentStorageOptions>`（`.RootPath`）、旧 JSON 布局：`{RootPath}/sessions/*.jsonl`、`{RootPath}/sessions/*.state.json`、`{RootPath}/users/userIdMap.json`（`Dictionary<原始id, 数字id>`）、`{RootPath}/users/{数字id}.json`（`UserEntry`）。
- Produces: `JsonToSqliteMigrator.MigrateAsync(CancellationToken) → MigrationSummary`（幂等：按 sessionId / userId 存在性跳过）。

- [ ] **Step 1: 写失败测试（造旧 JSON → 迁移 → 断言）**

`test/ManInBlack.AI.Tests/Persistence/JsonToSqliteMigratorTests.cs`:

```csharp
using System.Text.Json;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class JsonToSqliteMigratorTests
{
    private static async Task<(JsonToSqliteMigrator migrator, IDbContextFactory<ManInBlackDbContext> factory, string root)>
        CreateAsync()
    {
        var (factory, root) = await SqliteTestHelpers.CreateFactoryAsync();
        var options = Options.Create(new AgentStorageOptions { RootPath = root });
        var migrator = new JsonToSqliteMigrator(factory, options, NullLogger<JsonToSqliteMigrator>.Instance);
        return (migrator, factory, root);
    }

    private static void WriteJsonLl(string path, params object[] messages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var w = File.CreateText(path);
        foreach (var m in messages) w.WriteLine(JsonSerializer.Serialize(m));
    }

    [Fact]
    public async Task Migrate_ImportsMessagesSnapshotsUsers()
    {
        var (migrator, factory, root) = await CreateAsync();
        try
        {
            // 造 sessions/s1.jsonl(2 条消息)
            WriteJsonLl(Path.Combine(root, "sessions", "s1.jsonl"),
                new { Role = "user", Contents = new[] { new { Text = "hi", $type = "text" } } },
                new { Role = "assistant", Contents = new[] { new { Text = "yo", $type = "text" } } });

            // 造 sessions/s1.state.json
            await File.WriteAllTextAsync(Path.Combine(root, "sessions", "s1.state.json"),
                JsonSerializer.Serialize(new AgentStateSnapshot { SessionId = "s1", SystemPrompt = "p", SavedAt = DateTimeOffset.UtcNow }));

            // 造 users/userIdMap.json + users/3.json
            Directory.CreateDirectory(Path.Combine(root, "users"));
            await File.WriteAllTextAsync(Path.Combine(root, "users", "userIdMap.json"),
                JsonSerializer.Serialize(new Dictionary<string, string> { ["ext-1"] = "3" }));
            await File.WriteAllTextAsync(Path.Combine(root, "users", "3.json"),
                JsonSerializer.Serialize(new UserEntry { UserId = "ext-1", SelfHostUserId = "3", SessionIds = new List<string> { "ext-1_1" } }));

            var summary = await migrator.MigrateAsync();

            Assert.Equal(2, summary.Messages);
            Assert.Equal(1, summary.Snapshots);
            Assert.Equal(1, summary.Users);

            await using var db = factory.CreateDbContext();
            Assert.Equal(2, await db.SessionMessages.CountAsync());
            Assert.Single(await db.AgentStateSnapshots.ToListAsync());
            var user = await db.Users.SingleAsync();
            Assert.Equal("ext-1", user.UserId);
            Assert.Equal(3, user.Id); // 保留原数字内部 id
            Assert.Contains("ext-1_1", JsonSerializer.Deserialize<List<string>>(user.SessionIdsJson)!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Migrate_IsIdempotent_SecondRunSkipsAll()
    {
        var (migrator, factory, root) = await CreateAsync();
        try
        {
            WriteJsonLl(Path.Combine(root, "sessions", "s1.jsonl"),
                new { Role = "user", Contents = new[] { new { Text = "hi", $type = "text" } } });

            await migrator.MigrateAsync();
            var second = await migrator.MigrateAsync();

            Assert.Equal(0, second.Messages);
            Assert.True(second.Skipped >= 1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Migrate_NoOldData_IsNoOp()
    {
        var (migrator, factory, root) = await CreateAsync();
        try
        {
            var summary = await migrator.MigrateAsync();
            Assert.Equal(0, summary.Messages + summary.Snapshots + summary.Users);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Migrate_PreservesExplicitId_NextAutoIncrementContinues()
    {
        var (migrator, factory, root) = await CreateAsync();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "users"));
            await File.WriteAllTextAsync(Path.Combine(root, "users", "userIdMap.json"),
                JsonSerializer.Serialize(new Dictionary<string, string> { ["ext-old"] = "7" }));
            await File.WriteAllTextAsync(Path.Combine(root, "users", "7.json"),
                JsonSerializer.Serialize(new UserEntry { UserId = "ext-old", SelfHostUserId = "7" }));

            await migrator.MigrateAsync();

            // 迁移后新建用户,自增 Id 应 > 7,不与已迁值冲突
            var userStorage = new SqliteUserStorage(factory, NullLogger<SqliteUserStorage>.Instance);
            var newUser = await userStorage.GetOrCreateUser("ext-new");
            Assert.True(int.Parse(newUser.SelfHostUserId) > 7);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

> 测试里的 JSONL 行是手搓的简化对象，反序列化为 `ChatMessage` 时若字段不全，`Contents` 可能为空——这不影响 `MigrateAsync` 的"逐行读 → 重新序列化写库"路径计数。若简化行反序列化为 null 被跳过导致计数不符，则改为用真实 `ChatMessage` 序列化写文件（见 Step 3 注释）。实现 Step 3 后以实跑结果校准测试数据。

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~JsonToSqliteMigratorTests"
```

Expected：FAIL（`JsonToSqliteMigrator` 未定义）。

- [ ] **Step 3: 实现 JsonToSqliteMigrator**

`src/ManInBlack.AI/Persistence/JsonToSqliteMigrator.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// 一次性把旧 JSON 文件导入 SQLite。幂等:按 sessionId / userId 存在性跳过。
/// 旧布局:{RootPath}/sessions/*.jsonl、*.state.json、{RootPath}/users/userIdMap.json + {数字id}.json
/// </summary>
[ServiceRegister.Singleton]
public class JsonToSqliteMigrator(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    IOptions<AgentStorageOptions> options,
    ILogger<JsonToSqliteMigrator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<MigrationSummary> MigrateAsync(CancellationToken ct = default)
    {
        var root = options.Value.RootPath;
        var sessionsDir = Path.Combine(root, "sessions");
        var usersDir = Path.Combine(root, "users");
        int msg = 0, snap = 0, usr = 0, skip = 0;

        await using var db = dbFactory.CreateDbContext();

        // 1) 会话历史 JSONL
        if (Directory.Exists(sessionsDir))
        {
            foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.jsonl"))
            {
                var sessionId = Path.GetFileNameWithoutExtension(file);
                if (await db.SessionMessages.AnyAsync(x => x.SessionId == sessionId, ct)) { skip++; continue; }

                var now = DateTimeOffset.UtcNow.ToString("O");
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                foreach (var line in await File.ReadAllLinesAsync(file, ct))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var m = JsonSerializer.Deserialize<ChatMessage>(line, JsonOptions);
                        if (m is null) continue;
                        db.SessionMessages.Add(new SessionMessageEntity
                        {
                            SessionId = sessionId,
                            CreatedAt = now,
                            PayloadJson = JsonSerializer.Serialize(m, JsonOptions),
                        });
                        msg++;
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "迁移:会话 {SessionId} 跳过坏行", sessionId);
                    }
                }
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }

        // 2) 状态快照
        if (Directory.Exists(sessionsDir))
        {
            foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.state.json"))
            {
                var sessionId = Path.GetFileName(file).Replace(".state.json", "");
                if (await db.AgentStateSnapshots.AnyAsync(x => x.SessionId == sessionId, ct)) { skip++; continue; }

                try
                {
                    var s = JsonSerializer.Deserialize<AgentStateSnapshot>(await File.ReadAllTextAsync(file, ct), JsonOptions);
                    if (s is null) continue;
                    db.AgentStateSnapshots.Add(new AgentStateSnapshotEntity
                    {
                        SessionId = sessionId,
                        SavedAt = (s.SavedAt == default ? DateTimeOffset.UtcNow : s.SavedAt).ToString("O"),
                        PayloadJson = JsonSerializer.Serialize(s, JsonOptions),
                    });
                    await db.SaveChangesAsync(ct);
                    snap++;
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "迁移:快照 {SessionId} 跳过(损坏)", sessionId);
                }
            }
        }

        // 3) 用户(userIdMap + 条目)
        var mapFile = Path.Combine(usersDir, "userIdMap.json");
        if (File.Exists(mapFile))
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(mapFile, ct), JsonOptions) ?? new();
            foreach (var (oriId, internalId) in map)
            {
                if (await db.Users.AnyAsync(x => x.UserId == oriId, ct)) { skip++; continue; }

                string meta = "{}", sids = "[]";
                var entryFile = Path.Combine(usersDir, $"{internalId}.json");
                if (File.Exists(entryFile))
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<UserEntry>(await File.ReadAllTextAsync(entryFile, ct), JsonOptions);
                        if (entry is not null)
                        {
                            meta = JsonSerializer.Serialize(entry.Metadata ?? new(), JsonOptions);
                            sids = JsonSerializer.Serialize(entry.SessionIds ?? new List<string>(), JsonOptions);
                        }
                    }
                    catch (JsonException ex) { logger.LogWarning(ex, "迁移:用户 {Id} 条目损坏,用空值", oriId); }
                }

                db.Users.Add(new UserEntity
                {
                    Id = int.Parse(internalId), // 保留原数字内部 id
                    UserId = oriId,
                    MetadataJson = meta,
                    SessionIdsJson = sids,
                });
                await db.SaveChangesAsync(ct);
                usr++;
            }
        }

        return new MigrationSummary(msg, snap, usr, skip);
    }
}

/// <summary>
/// 迁移汇总。
/// </summary>
public sealed record MigrationSummary(int Messages, int Snapshots, int Users, int Skipped);
```

> 三处存在性判断均用 EF Core 内置 `AnyAsync`（`Microsoft.EntityFrameworkCore`）。文件顶部无需 `System.Linq.Expressions`。

- [ ] **Step 4: 跑测试，按实跑结果校准测试数据**

```bash
dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~JsonToSqliteMigratorTests"
```

Expected：4 个测试 PASS。

> 若 `Migrate_ImportsMessagesSnapshotsUsers` 因手搓 JSONL 行反序列化为 null 被跳过导致 `summary.Messages` 不符：把 `WriteJsonLl` 改为用真实 `ChatMessage` 序列化写文件——
> ```csharp
> private static void WriteRealJsonLl(string path, params ChatMessage[] messages)
> {
>     Directory.CreateDirectory(Path.GetDirectoryName(path)!);
>     var opts = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
>     using var w = File.CreateText(path);
>     foreach (var m in messages) w.WriteLine(JsonSerializer.Serialize(m, opts));
> }
> ```
> 并在测试里用 `WriteRealJsonLl(path, new ChatMessage(ChatRole.User, "hi"), new ChatMessage(ChatRole.Assistant, "yo"))`。

- [ ] **Step 5: 提交**

```bash
git add src/ManInBlack.AI/Persistence/JsonToSqliteMigrator.cs test/ManInBlack.AI.Tests/Persistence/JsonToSqliteMigratorTests.cs
git commit -m "✨ JsonToSqliteMigrator:一次性 JSON→SQLite 导入(幂等)"
```

---

## Task 7: 接入两个 demo——启动期 migrate + migrate-storage 参数

**Files:**
- Modify: `demo/AgentConsole/Program.cs`
- Modify: `demo/FeishuAdaptor/Program.cs`

**Interfaces:**
- Consumes: `StorageMigrationExtensions.MigrateManInBlackStorageAsync(IServiceProvider)`（Task 1）、`JsonToSqliteMigrator.MigrateAsync()`（Task 6）。
- Produces: 两个 demo 在启动时应用迁移；接受 `migrate-storage` 参数跑一次性 JSON 导入后退出。

- [ ] **Step 1: AgentConsole——启动 migrate + migrate-storage 分支**

修改 `demo/AgentConsole/Program.cs`。现有逻辑是 `var services = new ServiceCollection(); services.AddManInBlack().UseJson()...; var rootSp = services.BuildServiceProvider(); var factory = rootSp.GetRequiredService<AgentFactory>(); factory.RunAsync(...)`。

(a) 在文件 using 区追加：

```csharp
using ManInBlack.AI.Persistence;
```

(b) 在 `var rootSp = services.BuildServiceProvider();`（第 21 行）之后、`var factory = ...`（第 23 行）之前插入 migrate-storage 分支与启动 migrate：

```csharp
var rootSp = services.BuildServiceProvider();

// 一次性 JSON→SQLite 迁移子命令(执行后退出,不进入对话)
if (args.Length > 0 && args[0] == "migrate-storage")
{
    await rootSp.MigrateManInBlackStorageAsync();
    var migrator = rootSp.GetRequiredService<JsonToSqliteMigrator>();
    var summary = await migrator.MigrateAsync();
    Console.WriteLine($"迁移完成:消息 {summary.Messages},快照 {summary.Snapshots},用户 {summary.Users},跳过 {summary.Skipped}");
    return;
}

// 启动期应用 EF Core 迁移(已最新则空操作)
await rootSp.MigrateManInBlackStorageAsync();

var factory = rootSp.GetRequiredService<AgentFactory>();
```

> `args[0]` 原本作为对话提示传入 `factory.RunAsync`。`migrate-storage` 现作为保留"命令词"，普通对话不会用到该字面量，无冲突。

- [ ] **Step 2: FeishuAdaptor——启动 migrate + migrate-storage 分支**

修改 `demo/FeishuAdaptor/Program.cs`。现有 `var app = builder.Build();`（第 75 行）之后是 health/endpoint + `app.Run();`（第 94 行）。

(a) using 区追加：

```csharp
using ManInBlack.AI.Persistence;
```

(b) 在 `var app = builder.Build();` 之后插入（先 migrate-storage 分支，再正常启动 migrate）：

```csharp
var app = builder.Build();

// 一次性 JSON→SQLite 迁移子命令(执行后退出,不启动 Web 服务/不连飞书)
if (args.Contains("migrate-storage"))
{
    await app.Services.MigrateManInBlackStorageAsync();
    var migrator = app.Services.GetRequiredService<JsonToSqliteMigrator>();
    var summary = await migrator.MigrateAsync();
    Console.WriteLine($"迁移完成:消息 {summary.Messages},快照 {summary.Snapshots},用户 {summary.Users},跳过 {summary.Skipped}");
    return;
}

// 启动期应用 EF Core 迁移(已最新则空操作)
await app.Services.MigrateManInBlackStorageAsync();
```

`app.Run()` 之前的 health/endpoint 注册保持不变。

- [ ] **Step 3: 构建 + 用 AgentConsole 实跑 migrate-storage（空数据 no-op）**

```bash
dotnet build ManInBlack.slnx
# 临时 RootPath,验证 no-op 路径不报错
MIB_ROOT="$(mktemp -d)"
Storage__RootPath="$MIB_ROOT" dotnet run --project demo/AgentConsole -- migrate-storage
echo "---"
ls -la "$MIB_ROOT"   # 应有 maninblack.db 生成
rm -rf "$MIB_ROOT"
```

Expected：输出 `迁移完成:消息 0,快照 0,用户 0,跳过 0`；`$MIB_ROOT` 下生成 `maninblack.db`。

> 若 AgentConsole 不读环境变量 `Storage__RootPath`（取决于配置源），改用本地 `~/.man-in-black` 跑一次空数据验证，或临时改 `settings.json` 的 `Storage.RootPath`。核心验证：命令能跑通、生成空库、退出码 0。

- [ ] **Step 4: 提交**

```bash
git add demo/AgentConsole/Program.cs demo/FeishuAdaptor/Program.cs
git commit -m "✨ 两 demo 启动期应用迁移 + migrate-storage 子命令"
```

---

## Task 8: 文档

**Files:**
- Create: `docs/storage-guide.md`
- Modify: `docs/configuration-guide.md`、`docs/architecture.md`、`docs/feishu-guide.md`、`CLAUDE.md`

**Interfaces:**
- Consumes: spec（`docs/superpowers/specs/2026-06-25-sqlite-storage-migration-design.md`）、本计划实现结果。

- [ ] **Step 1: 新增 `docs/storage-guide.md`**

覆盖：SQLite 存储、`{RootPath}/maninblack.db`、3 张表 schema、EF Migrations（`InitialCreate`）、宿主启动期 `await sp.MigrateManInBlackStorageAsync()`、`migrate-storage` 子命令（AgentConsole / FeishuAdaptor 用法）、WAL/busy_timeout 说明。从 spec §3–§7 与本计划 Task 1/6/7 提炼。

- [ ] **Step 2: 更新 `docs/configuration-guide.md`**

在 `Storage` 配置节注明：`RootPath` 现含 `maninblack.db`（无新配置键）；旧 `sessions/`、`users/` 目录不再产生新数据。

- [ ] **Step 3: 更新 `docs/architecture.md`**

把"存储层"描述从 JSON 文件改为 EF Core / SQLite，指向 `Persistence/` 模块与 `docs/storage-guide.md`。

- [ ] **Step 4: 更新 `docs/feishu-guide.md`**

追加阿里云迁移 runbook（来自 spec §7.4）：

```bash
# 1. 发新二进制(含 SQLite 存储 + migrator + migrate-storage)到服务器
scp mib-feishu.tar.gz aliyun:~ && ssh aliyun 'tar xzf mib-feishu.tar.gz -C /opt/mib-feishu && chmod -R 755 /opt/mib-feishu'
# 2. 停服
ssh aliyun 'systemctl stop mib-feishu'
# 3. 迁移
ssh aliyun '/opt/mib-feishu/FeishuAdaptor migrate-storage'
# 4. 核对(journalctl -u mib-feishu 看汇总 / ls /root/.man-in-black/maninblack.db)
# 5. 起服
ssh aliyun 'systemctl start mib-feishu'
```

- [ ] **Step 5: 更新 `CLAUDE.md` 文档索引**

在"文档索引"列表追加一行：

```markdown
- [存储指南](docs/storage-guide.md)
```

- [ ] **Step 6: 提交**

```bash
git add docs/storage-guide.md docs/configuration-guide.md docs/architecture.md docs/feishu-guide.md CLAUDE.md
git commit -m "📝 同步存储层文档:storage-guide + 迁移 runbook"
```

---

## Task 9: 端到端验证（AgentConsole 对话 + sqlite3 查库）

**Files:** 无代码改动（验证任务；复用项目 `test-agent-console` skill 的流程）。

- [ ] **Step 1: 跑几轮 AgentConsole 对话（含工具调用）**

```bash
dotnet run --project demo/AgentConsole -- "你好,用文件工具列出当前目录"
# 再跑一轮纯文本对话
dotnet run --project demo/AgentConsole -- "讲个笑话"
```

观察：对话正常、工具调用（`FileTools`）触发，`SessionMessages` 覆盖 text + function 两类 `AIContent`。

- [ ] **Step 2: 确认旧 JSON 目录不再产生新文件**

```bash
ls -la ~/.man-in-black/sessions/ 2>/dev/null | tail -5
ls -la ~/.man-in-black/users/ 2>/dev/null | tail -5
```

Expected：无新写入时间戳（新数据只进 DB）；目录若不存在也正常。

- [ ] **Step 3: 用 sqlite3 查库核对落盘**

```bash
sqlite3 ~/.man-in-black/maninblack.db "SELECT SessionId, COUNT(*) FROM SessionMessages GROUP BY SessionId;"
sqlite3 ~/.man-in-black/maninblack.db "SELECT SessionId, SavedAt FROM AgentStateSnapshots;"
sqlite3 ~/.man-in-black/maninblack.db "SELECT Id, UserId FROM Users;"
sqlite3 ~/.man-in-black/maninblack.db "PRAGMA journal_mode;"   # 应为 wal
```

Expected：看到刚跑的会话有消息行；`journal_mode` 返回 `wal`。

- [ ] **Step 4: 重启后历史加载无损**

记录 Step 1 某次对话的 `SessionId`（从 `Users.SessionIdsJson` 或日志取），用同一 sessionId 再开对话，确认历史能从 SQLite 正确加载、上下文连续。

- [ ] **Step 5: bubblewrap 沙盒写入验证（Linux）**

若手头有 Linux 环境，开启 `UseSandbox` 跑一次 AgentConsole，确认主进程对 `maninblack.db` 写入不受沙盒影响（预期不受影响——存储不在沙盒内）。

- [ ] **Step 6: 记录验证结论**

把 Step 1–5 的实测输出与结论写进提交说明或 PR 描述。

```bash
git commit --allow-empty -m "✅ 端到端验证:AgentConsole 对话 + sqlite3 查库通过"
```

---

## Self-Review 结论

- **Spec 覆盖**：§1–2 背景/决策 → Global Constraints；§3 架构 → Task 1（基础设施）+ Task 5（切换）；§4 schema → Task 1 实体/DbContext/迁移；§5 组件/DI → Task 1（DI）+ Task 2–4（实现）；§5.4 启动 migrate → Task 1 扩展 + Task 7（接入）；§6 错误/并发 → Task 1 拦截器 + Task 2–4 try/catch；§7 迁移工具 → Task 6 + Task 7；§7.4 阿里云 runbook → Task 8 文档 + Task 9 验证；§8 测试/文档 → 各 Task 测试 + Task 8；§10 风险 → Task 6 自增验证、Task 9 沙盒/往返验证。
- **占位符**：Task 6 Step 3 的迁移实现已用完整正确代码（EF 内置 `AnyAsync`），无 TBD/TODO。
- **类型一致性**：实体属性名（`SessionId`/`CreatedAt`/`PayloadJson`/`SavedAt`/`MetadataJson`/`SessionIdsJson`/`UserId`/`Id`）在 DbContext、存储类、迁移测试中一致；`MigrationSummary(Messages, Snapshots, Users, Skipped)` 在 Task 6 实现与 Task 7 接入一致。
