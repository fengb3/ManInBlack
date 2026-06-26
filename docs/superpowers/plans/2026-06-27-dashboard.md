# ManInBlack Dashboard 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `demo/Dashboard/` 新增团队部署常驻的只读 Web 应用,浏览器查看 SQLite 里的会话消息/用户并支持全文搜索,前端为 Vite + React + TypeScript。

**Architecture:** 独立 ASP.NET Core Minimal API(`Microsoft.NET.Sdk.Web`),**不调用** `AddManInBlack()`,自注册只读 `IDbContextFactory<ManInBlackDbContext>` 直查 SQLite。React SPA 由 Vite 构建,产物落到 `wwwroot/` 由 ASP.NET 托管;API 返回 camelCase JSON,React `fetch(credentials:'include')` 消费,cookie 鉴权(fail-closed)。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、EF Core Sqlite 10.0.0、Microsoft.Extensions.AI 10.5.0、xunit;React 18.3 + react-markdown 9 + Vite 6 + TypeScript。

## Global Constraints

- 目标框架 `net10.0`,`<Nullable>enable</Nullable>` `<ImplicitUsings>enable</ImplicitUsings>`。
- **不调用** `AddManInBlack()`;`Program.cs` 自行 `Configure<AgentStorageOptions>` + 自注册只读 DbContextFactory(`Mode=ReadOnly`),不跑迁移。
- **Fail-closed**:`Dashboard:Password` 未配置/空白 → 启动抛 `InvalidOperationException`。
- 包版本固定:`Microsoft.EntityFrameworkCore.Sqlite` 10.0.0、`Microsoft.Extensions.AI` 10.5.0、React 18.3.1、react-markdown 9.0.x、Vite 6.x、`@vitejs/plugin-react` 4.x。
- API JSON 统一 camelCase + `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`,前后端枚举字符串对齐(`text`/`toolCall`/`toolResult`/`unknown`)。
- 所有注释/文档使用中文;提交信息 gitmoji 前缀,**禁止** `Co-authored-by`。
- 发布机需 Node(`npm`);运行时仅 .NET + 静态文件,无需 Node。

---

## 文件结构总览

| 文件 | 职责 |
|---|---|
| `demo/Dashboard/Dashboard.csproj` | Web SDK 项目 + MSBuild publish target |
| `demo/Dashboard/Program.cs` | 宿主、DI、只读 DbContextFactory、cookie 鉴权、全部端点 |
| `demo/Dashboard/Auth/DashboardOptions.cs` | `{ Password }` 配置 + `AuthService`(密码校验、fail-closed) |
| `demo/Dashboard/Data/ReadModels.cs` | `SessionSummary`/`UserSummary`/`MessageBlock`/`MessageView`/`SearchResult`/`MessageBlockKind` |
| `demo/Dashboard/Data/ChatMessageRenderer.cs` | `ChatMessage` → `MessageView`(纯映射) |
| `demo/Dashboard/Data/ChatHistoryQueries.cs` | 直查 DbContext:会话/用户/单会话/搜索 |
| `demo/Dashboard/wwwroot/.gitignore` | 忽略构建产物 |
| `demo/Dashboard/Properties/launchSettings.json` | :5080 端口 |
| `demo/Dashboard/client/**` | Vite + React + TS 源码 |
| `test/Dashboard.Tests/**` | xunit 测试 + 本地 SqliteTestHelper |

---

## Task 1: 项目脚手架 + ReadModels

**Files:**
- Create: `demo/Dashboard/Dashboard.csproj`
- Create: `demo/Dashboard/Data/ReadModels.cs`
- Create: `demo/Dashboard/wwwroot/.gitignore`
- Create: `test/Dashboard.Tests/Dashboard.Tests.csproj`
- Create: `test/Dashboard.Tests/Helpers/SqliteTestHelper.cs`

**Interfaces:**
- Produces: `ManInBlack.Dashboard.Data` 命名空间下的 `SessionSummary`、`UserSummary`、`MessageBlock`、`MessageView`、`SearchResult`、`MessageBlockKind`;`Dashboard.Tests.Helpers.SqliteTestHelper.CreateAsync()` 返回 `(IDbContextFactory<ManInBlackDbContext> factory, ServiceProvider sp, string rootPath)`,工厂已迁移、可写(供测试种子用)。

- [ ] **Step 1: 创建 `demo/Dashboard/Dashboard.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.5.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ManInBlack.AI\ManInBlack.AI.csproj" />
  </ItemGroup>
  <Target Name="BuildClient" BeforeTargets="Publish">
    <Exec Command="npm ci" WorkingDirectory="client" Condition="Exists('client/package-lock.json')" />
    <Exec Command="npm install" WorkingDirectory="client" Condition="!Exists('client/package-lock.json')" />
    <Exec Command="npm run build" WorkingDirectory="client" />
  </Target>
</Project>
```

- [ ] **Step 2: 创建 `demo/Dashboard/Data/ReadModels.cs`**

```csharp
namespace ManInBlack.Dashboard.Data;

public sealed record SessionSummary
{
    public required string SessionId { get; init; }
    public required int MessageCount { get; init; }
    public required string FirstAt { get; init; }
    public required string LastAt { get; init; }
    public string? UserId { get; init; }
}

public sealed record UserSummary
{
    public required string UserId { get; init; }
    public required Dictionary<string, object?> Metadata { get; init; }
    public required IReadOnlyList<string> SessionIds { get; init; }
}

public enum MessageBlockKind { Text, ToolCall, ToolResult, Unknown }

public sealed record MessageBlock
{
    public required MessageBlockKind Kind { get; init; }
    public string? Text { get; init; }          // Text
    public string? ToolName { get; init; }       // ToolCall
    public string? ArgumentsJson { get; init; }  // ToolCall
    public string? ResultJson { get; init; }     // ToolResult
    public string? RawJson { get; init; }        // Unknown
}

public sealed record MessageView
{
    public required string Role { get; init; }
    public required IReadOnlyList<MessageBlock> Blocks { get; init; }
}

public sealed record SearchResult
{
    public required string SessionId { get; init; }
    public required string Snippet { get; init; }
    public required string CreatedAt { get; init; }
}
```

- [ ] **Step 3: 创建 `demo/Dashboard/wwwroot/.gitignore`**

```
*
!.gitignore
```

- [ ] **Step 4: 创建 `test/Dashboard.Tests/Dashboard.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="*" />
    <PackageReference Include="xunit" Version="*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\demo\Dashboard\Dashboard.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: 创建 `test/Dashboard.Tests/Helpers/SqliteTestHelper.cs`**

```csharp
using ManInBlack.AI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dashboard.Tests.Helpers;

public static class SqliteTestHelper
{
    /// <summary>临时目录建一个已迁移、可写的 SQLite 工厂(供测试种子)。调用方负责释放 sp 后清理 root。</summary>
    public static async Task<(IDbContextFactory<ManInBlackDbContext> factory, ServiceProvider sp, string rootPath)> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mib_dash_{Guid.NewGuid()}");
        Directory.CreateDirectory(root);
        var services = new ServiceCollection();
        services.AddDbContextFactory<ManInBlackDbContext>(o =>
            o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")}"));
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<ManInBlackDbContext>>();
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        return (factory, sp, root);
    }
}
```

- [ ] **Step 6: 加入解决方案**

Run:
```bash
dotnet sln ManInBlack.slnx add demo/Dashboard/Dashboard.csproj
dotnet sln ManInBlack.slnx add test/Dashboard.Tests/Dashboard.Tests.csproj
```
Expected: 两条均输出 `Project ... was added.`。

- [ ] **Step 7: 验证构建**

Run: `dotnet build demo/Dashboard/Dashboard.csproj`
Expected: BUILD SUCCEEDED(此时无 Program.cs,仅类库式编译;若 SDK 要求入口,下一个 Task 补 Program.cs)。

> 说明:Web SDK 项目无 Program.cs 时 `dotnet build` 仍可编译类文件;`dotnet run` 会失败,Task 5 补齐入口。

- [ ] **Step 8: Commit**

```bash
git add demo/Dashboard test/Dashboard.Tests ManInBlack.slnx
git commit -m "🎉 Dashboard 脚手架:项目 + ReadModels + 测试工程"
```

---

## Task 2: ChatMessageRenderer(纯映射,TDD)

**Files:**
- Create: `demo/Dashboard/Data/ChatMessageRenderer.cs`
- Test: `test/Dashboard.Tests/ChatMessageRendererTests.cs`

**Interfaces:**
- Consumes: `Microsoft.Extensions.AI.ChatMessage`(来自 10.5.0);Task 1 的 `MessageView`/`MessageBlock`/`MessageBlockKind`。
- Produces: `ChatMessageRenderer.Render(ChatMessage) → MessageView`。

- [ ] **Step 1: 写失败测试 `test/Dashboard.Tests/ChatMessageRendererTests.cs`**

```csharp
using ManInBlack.Dashboard.Data;
using Microsoft.Extensions.AI;
using Xunit;

namespace Dashboard.Tests;

public class ChatMessageRendererTests
{
    [Fact]
    public void Render_Text_MapsTextBlock()
    {
        var msg = new ChatMessage(ChatRole.User, "hello");
        var view = ChatMessageRenderer.Render(msg);
        Assert.Equal("user", view.Role);
        var b = Assert.Single(view.Blocks);
        Assert.Equal(MessageBlockKind.Text, b.Kind);
        Assert.Equal("hello", b.Text);
    }

    [Fact]
    public void Render_FunctionCall_MapsToolCallBlock()
    {
        var msg = new ChatMessage(ChatRole.Assistant, []);
        msg.Contents.Add(new FunctionCallContent("call_1", "read_file",
            new Dictionary<string, object?> { ["path"] = "/a" }));
        var view = ChatMessageRenderer.Render(msg);
        var b = Assert.Single(view.Blocks);
        Assert.Equal(MessageBlockKind.ToolCall, b.Kind);
        Assert.Equal("read_file", b.ToolName);
        Assert.Contains("path", b.ArgumentsJson!);
    }

    [Fact]
    public void Render_FunctionResult_MapsToolResultBlock()
    {
        var msg = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "ok-text")]);
        var view = ChatMessageRenderer.Render(msg);
        var b = Assert.Single(view.Blocks);
        Assert.Equal(MessageBlockKind.ToolResult, b.Kind);
        Assert.Equal("ok-text", b.ResultJson);
    }

    [Fact]
    public void Render_UnknownContent_MapsUnknownBlock()
    {
        var msg = new ChatMessage(ChatRole.System, [new OtherContent()]);
        var view = ChatMessageRenderer.Render(msg);
        var b = Assert.Single(view.Blocks);
        Assert.Equal(MessageBlockKind.Unknown, b.Kind);
        Assert.NotNull(b.RawJson);
    }

    sealed class OtherContent : AIContent { }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/Dashboard.Tests --filter "FullyQualifiedName~ChatMessageRendererTests"`
Expected: FAIL(编译错误:`ChatMessageRenderer` 未定义)。

- [ ] **Step 3: 实现 `demo/Dashboard/Data/ChatMessageRenderer.cs`**

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ManInBlack.Dashboard.Data;

/// <summary>把 ChatMessage 内容块映射成前端友好的 MessageView(纯函数,无 DB)。</summary>
public static class ChatMessageRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static MessageView Render(ChatMessage message)
    {
        var blocks = new List<MessageBlock>(message.Contents.Count);
        foreach (var content in message.Contents)
        {
            blocks.Add(content switch
            {
                TextContent t => new MessageBlock { Kind = MessageBlockKind.Text, Text = t.Text },
                FunctionCallContent fc => new MessageBlock
                {
                    Kind = MessageBlockKind.ToolCall,
                    ToolName = fc.Name,
                    ArgumentsJson = JsonSerializer.Serialize(fc.Arguments, JsonOptions),
                },
                FunctionResultContent fr => new MessageBlock
                {
                    Kind = MessageBlockKind.ToolResult,
                    ResultJson = fr.Result switch
                    {
                        null => "null",
                        string s => s,
                        _ => JsonSerializer.Serialize(fr.Result, JsonOptions),
                    },
                },
                _ => new MessageBlock { Kind = MessageBlockKind.Unknown, RawJson = JsonSerializer.Serialize(content, JsonOptions) },
            });
        }
        return new MessageView { Role = message.Role.Value, Blocks = blocks };
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test test/Dashboard.Tests --filter "FullyQualifiedName~ChatMessageRendererTests"`
Expected: PASS(4 个测试全绿)。

- [ ] **Step 5: Commit**

```bash
git add demo/Dashboard/Data/ChatMessageRenderer.cs test/Dashboard.Tests/ChatMessageRendererTests.cs
git commit -m "✨ ChatMessageRenderer:ChatMessage → MessageView 映射"
```

---

## Task 3: ChatHistoryQueries(直查 DbContext,TDD)

**Files:**
- Create: `demo/Dashboard/Data/ChatHistoryQueries.cs`
- Test: `test/Dashboard.Tests/ChatHistoryQueriesTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<ManInBlackDbContext>`(ManInBlack.AI.Persistence);实体 `SessionMessageEntity`/`UserEntity`;Task 1 的 ReadModels;Task 2 的 `ChatMessageRenderer.Render`。
- Produces: `ChatHistoryQueries` 的四个方法(签名见 Step 3)。

- [ ] **Step 1: 写失败测试 `test/Dashboard.Tests/ChatHistoryQueriesTests.cs`**

```csharp
using System.Text.Json;
using Dashboard.Tests.Helpers;
using ManInBlack.Dashboard.Data;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dashboard.Tests;

public class ChatHistoryQueriesTests
{
    private static ChatHistoryQueries NewQueries(IDbContextFactory<ManInBlackDbContext> factory) =>
        new(factory, NullLogger<ChatHistoryQueries>.Instance);

    [Fact]
    public async Task ListSessions_GroupsBySession_AndMapsUser()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await SeedAsync(factory);
            var q = NewQueries(factory);
            var sessions = await q.ListSessionsAsync();

            Assert.Equal(2, sessions.Count); // s1, s2
            var s1 = sessions.Single(s => s.SessionId == "s1");
            Assert.Equal(2, s1.MessageCount);
            Assert.Equal("2026-01-01T00:00:00Z", s1.FirstAt);
            Assert.Equal("2026-01-02T00:00:00Z", s1.LastAt);
            Assert.Equal("u1", s1.UserId); // 关联用户
        }
        finally { sp.Dispose(); TryDelete(root); }
    }

    [Fact]
    public async Task GetSessionMessages_SkipsCorruptRows()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await SeedAsync(factory);
            var q = NewQueries(factory);
            var s1 = await q.GetSessionMessagesAsync("s1");
            Assert.Equal(2, s1.Count); // 两条均解析
            var s2 = await q.GetSessionMessagesAsync("s2");
            Assert.Empty(s2); // 损坏行被跳过
        }
        finally { sp.Dispose(); TryDelete(root); }
    }

    [Fact]
    public async Task ListUsers_DeserializesSessionsAndMetadata()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await SeedAsync(factory);
            var q = NewQueries(factory);
            var users = await q.ListUsersAsync();
            var u = Assert.Single(users);
            Assert.Equal("u1", u.UserId);
            Assert.Contains("s1", u.SessionIds);
        }
        finally { sp.Dispose(); TryDelete(root); }
    }

    [Fact]
    public async Task Search_HitsByPayload_AndEmptyQueryReturnsNothing()
    {
        var (factory, sp, root) = await SqliteTestHelper.CreateAsync();
        try
        {
            await SeedAsync(factory);
            var q = NewQueries(factory);
            var hits = await q.SearchAsync("hello");
            Assert.Contains(hits, r => r.SessionId == "s1");
            Assert.Empty(await q.SearchAsync(""));
        }
        finally { sp.Dispose(); TryDelete(root); }
    }

    private static async Task SeedAsync(IDbContextFactory<ManInBlackDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.SessionMessages.Add(new SessionMessageEntity { SessionId = "s1", CreatedAt = "2026-01-01T00:00:00Z", PayloadJson = JsonSerializer.Serialize(new ChatMessage(ChatRole.User, "hello")) });
        db.SessionMessages.Add(new SessionMessageEntity { SessionId = "s1", CreatedAt = "2026-01-02T00:00:00Z", PayloadJson = JsonSerializer.Serialize(new ChatMessage(ChatRole.Assistant, "hi")) });
        db.SessionMessages.Add(new SessionMessageEntity { SessionId = "s2", CreatedAt = "2026-01-03T00:00:00Z", PayloadJson = "{not-json" });
        db.Users.Add(new UserEntity { UserId = "u1", MetadataJson = "{}", SessionIdsJson = JsonSerializer.Serialize(new List<string> { "s1" }) });
        await db.SaveChangesAsync();
    }

    private static void TryDelete(string root) { try { Directory.Delete(root, true); } catch (IOException) { } }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/Dashboard.Tests --filter "FullyQualifiedName~ChatHistoryQueriesTests"`
Expected: FAIL(`ChatHistoryQueries` 未定义)。

- [ ] **Step 3: 实现 `demo/Dashboard/Data/ChatHistoryQueries.cs`**

```csharp
using System.Text.Json;
using ManInBlack.AI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.Dashboard.Data;

/// <summary>直查 DbContext 的只读历史查询(会话/用户/单会话/搜索)。</summary>
public sealed class ChatHistoryQueries(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<ChatHistoryQueries> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var groups = await db.SessionMessages.AsNoTracking()
            .GroupBy(x => x.SessionId)
            .Select(g => new
            {
                SessionId = g.Key,
                Count = g.Count(),
                First = g.Min(x => x.CreatedAt),
                Last = g.Max(x => x.CreatedAt),
            })
            .ToListAsync(ct);

        var userBySession = await BuildSessionToUserMapAsync(db, ct);

        return groups
            .OrderByDescending(g => g.Last)
            .Select(g => new SessionSummary
            {
                SessionId = g.SessionId,
                MessageCount = g.Count,
                FirstAt = g.First,
                LastAt = g.Last,
                UserId = userBySession.GetValueOrDefault(g.SessionId),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.Users.AsNoTracking().ToListAsync(ct);

        var list = new List<UserSummary>(rows.Count);
        foreach (var u in rows)
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(u.MetadataJson) ?? new();
                var sessionIds = JsonSerializer.Deserialize<List<string>>(u.SessionIdsJson) ?? new();
                list.Add(new UserSummary { UserId = u.UserId, Metadata = metadata, SessionIds = sessionIds });
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "用户 {UserId} 元数据反序列化失败,跳过", u.UserId);
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<MessageView>> GetSessionMessagesAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.SessionMessages.AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        var views = new List<MessageView>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<ChatMessage>(row.PayloadJson, JsonOptions);
                if (msg is not null) views.Add(ChatMessageRenderer.Render(msg));
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "会话 {SessionId} 第 {Id} 行消息反序列化失败,跳过", sessionId, row.Id);
            }
        }
        return views;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchResult>();

        await using var db = dbFactory.CreateDbContext();
        var rows = await db.SessionMessages.AsNoTracking()
            .Where(x => EF.Functions.Like(x.PayloadJson, $"%{query}%"))
            .OrderByDescending(x => x.Id)
            .Take(200)
            .Select(x => new { x.SessionId, x.PayloadJson, x.CreatedAt })
            .ToListAsync(ct);

        return rows.Select(r => new SearchResult
        {
            SessionId = r.SessionId,
            CreatedAt = r.CreatedAt,
            Snippet = MakeSnippet(r.PayloadJson, query),
        }).ToList();
    }

    private static async Task<Dictionary<string, string>> BuildSessionToUserMapAsync(ManInBlackDbContext db, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        var map = new Dictionary<string, string>();
        foreach (var u in users)
        {
            try
            {
                var ids = JsonSerializer.Deserialize<List<string>>(u.SessionIdsJson);
                if (ids is null) continue;
                foreach (var sid in ids) map.TryAdd(sid, u.UserId);
            }
            catch (JsonException) { /* 忽略单用户解析失败 */ }
        }
        return map;
    }

    private static string MakeSnippet(string payload, string query, int radius = 60)
    {
        var idx = payload.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return payload.Length <= 120 ? payload : payload[..120];
        var start = Math.Max(0, idx - radius);
        var len = Math.Min(payload.Length - start, query.Length + radius * 2);
        var snippet = payload.Substring(start, len);
        return (start > 0 ? "…" : "") + snippet + (start + len < payload.Length ? "…" : "");
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test test/Dashboard.Tests --filter "FullyQualifiedName~ChatHistoryQueriesTests"`
Expected: PASS(4 个测试全绿)。

- [ ] **Step 5: Commit**

```bash
git add demo/Dashboard/Data/ChatHistoryQueries.cs test/Dashboard.Tests/ChatHistoryQueriesTests.cs
git commit -m "✨ ChatHistoryQueries:会话/用户/消息/搜索只读查询"
```

---

## Task 4: AuthService(密码校验 + fail-closed,TDD)

**Files:**
- Create: `demo/Dashboard/Auth/DashboardOptions.cs`
- Test: `test/Dashboard.Tests/AuthServiceTests.cs`

**Interfaces:**
- Consumes: 无外部依赖(纯 + BCL 加密)。
- Produces: `ManInBlack.Dashboard.Auth.DashboardOptions { string? Password }`、`AuthService.VerifyPassword(string?, string?) → bool`、`AuthService.EnsureConfigured(DashboardOptions) → void`(失败抛 `InvalidOperationException`)。

- [ ] **Step 1: 写失败测试 `test/Dashboard.Tests/AuthServiceTests.cs`**

```csharp
using ManInBlack.Dashboard.Auth;
using Xunit;

namespace Dashboard.Tests;

public class AuthServiceTests
{
    [Fact]
    public void VerifyPassword_Correct_ReturnsTrue() =>
        Assert.True(AuthService.VerifyPassword("s3cret", "s3cret"));

    [Fact]
    public void VerifyPassword_Wrong_ReturnsFalse() =>
        Assert.False(AuthService.VerifyPassword("s3cret", "wrong"));

    [Fact]
    public void VerifyPassword_Empty_ReturnsFalse() =>
        Assert.False(AuthService.VerifyPassword("", "x"));

    [Fact]
    public void EnsureConfigured_Empty_Throws() =>
        Assert.Throws<InvalidOperationException>(() => AuthService.EnsureConfigured(new DashboardOptions()));

    [Fact]
    public void EnsureConfigured_Set_DoesNotThrow() =>
        AuthService.EnsureConfigured(new DashboardOptions { Password = "x" });
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/Dashboard.Tests --filter "FullyQualifiedName~AuthServiceTests"`
Expected: FAIL(类型未定义)。

- [ ] **Step 3: 实现 `demo/Dashboard/Auth/DashboardOptions.cs`**

```csharp
using System.Security.Cryptography;

namespace ManInBlack.Dashboard.Auth;

/// <summary>Dashboard 配置节(对应 settings.json 的 Dashboard:*)。</summary>
public sealed class DashboardOptions
{
    public string? Password { get; set; }
}

/// <summary>密码校验与启动期 fail-closed 检查(纯静态,便于测试)。</summary>
public static class AuthService
{
    /// <summary>固定时长比对,防计时侧信道。长度不同直接返回 false。</summary>
    public static bool VerifyPassword(string? stored, string? supplied)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(supplied)) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(stored);
        var b = System.Text.Encoding.UTF8.GetBytes(supplied);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Fail-closed:未配置密码直接抛异常,拒绝启动。</summary>
    public static void EnsureConfigured(DashboardOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Password))
            throw new InvalidOperationException("settings.json 缺少 Dashboard:Password,Dashboard 拒绝启动(fail-closed)。");
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test test/Dashboard.Tests --filter "FullyQualifiedName~AuthServiceTests"`
Expected: PASS(5 个测试全绿)。

- [ ] **Step 5: 跑全量测试**

Run: `dotnet test test/Dashboard.Tests`
Expected: PASS(全部 Tasks 2-4 测试)。

- [ ] **Step 6: Commit**

```bash
git add demo/Dashboard/Auth/DashboardOptions.cs test/Dashboard.Tests/AuthServiceTests.cs
git commit -m "✨ AuthService:固定时长密码校验 + fail-closed"
```

---

## Task 5: Program.cs 宿主 + DI + 鉴权 + API 端点

**Files:**
- Create: `demo/Dashboard/Program.cs`
- Create: `demo/Dashboard/Properties/launchSettings.json`

**Interfaces:**
- Consumes: Task 1-4 全部产物;`ManInBlack.AI.Configuration.ManInBlackConfigurationBuilder.AddManInBlackSettings()`;`ManInBlack.AI.Persistence.ManInBlackDbContext`;`ManInBlack.AI.Abstraction.Storage.AgentStorageOptions`。
- Produces: 运行中的 Web 服务,端点见 Step 1。

- [ ] **Step 1: 创建 `demo/Dashboard/Program.cs`**

```csharp
using System.Security.Claims;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Persistence;
using ManInBlack.Dashboard.Auth;
using ManInBlack.Dashboard.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// 1) 配置源:复用 ~/.man-in-black/settings.json
builder.Configuration.AddManInBlackSettings();

// 2) 绑定 Storage 节 + Dashboard 节
builder.Services.Configure<AgentStorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));

// 3) 自注册只读 DbContextFactory(不调用 AddManInBlack,不跑迁移)
builder.Services.AddDbContextFactory<ManInBlackDbContext>((sp, o) =>
{
    var root = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.RootPath;
    o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")};Mode=ReadOnly");
});

builder.Services.AddSingleton<ChatHistoryQueries>();

// 4) API JSON:camelCase + 枚举字符串(camelCase),与前端 TS 类型对齐
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// 5) Cookie 鉴权:API 返回 401 而非 302 跳转(适配 SPA fetch)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Fail-closed:无密码拒启动
AuthService.EnsureConfigured(app.Services.GetRequiredService<IOptions<DashboardOptions>>().Value);

app.UseAuthentication();
app.UseAuthorization();

// 静态文件 + SPA 回退(wwwroot 由 Vite 构建产物填充)
app.UseDefaultFiles();
app.UseStaticFiles();

// 鉴权端点
app.MapGet("/api/me", (HttpContext ctx) =>
    Results.Ok(new { authenticated = ctx.User.Identity?.IsAuthenticated == true }))
    .AllowAnonymous();

app.MapPost("/api/login", async (LoginRequest req, IOptions<DashboardOptions> opts, HttpContext ctx) =>
{
    if (!AuthService.VerifyPassword(opts.Value.Password, req.Password))
        return Results.Unauthorized();
    var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Ok(new { authenticated = true });
}).AllowAnonymous();

app.MapPost("/api/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
}).RequireAuthorization();

// 数据端点(均需登录)
app.MapGet("/api/sessions", async (ChatHistoryQueries q, CancellationToken ct) =>
    Results.Ok(await q.ListSessionsAsync(ct))).RequireAuthorization();

app.MapGet("/api/sessions/{sessionId}/messages", async (string sessionId, ChatHistoryQueries q, CancellationToken ct) =>
    Results.Ok(await q.GetSessionMessagesAsync(sessionId, ct))).RequireAuthorization();

app.MapGet("/api/users", async (ChatHistoryQueries q, CancellationToken ct) =>
    Results.Ok(await q.ListUsersAsync(ct))).RequireAuthorization();

app.MapGet("/api/search", async (string? q, ChatHistoryQueries queries, CancellationToken ct) =>
    Results.Ok(await queries.SearchAsync(q ?? "", ct))).RequireAuthorization();

app.MapFallbackToFile("index.html");

app.Run();

public sealed record LoginRequest(string Password);
```

- [ ] **Step 2: 创建 `demo/Dashboard/Properties/launchSettings.json`**

```json
{
  "profiles": {
    "Dashboard": {
      "commandName": "Project",
      "applicationUrl": "http://localhost:5080",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
    }
  }
}
```

- [ ] **Step 3: 构建确认**

Run: `dotnet build demo/Dashboard/Dashboard.csproj`
Expected: BUILD SUCCEEDED。

- [ ] **Step 4: 准备本地配置**

确认 `~/.man-in-black/settings.json` 含 `Dashboard` 节;若无,手动加入(替换为真实密码):
```json
"Dashboard": { "Password": "dev-only-test-password" }
```
并确认 DB 文件存在(`~/.man-in-black/maninblack.db`)。若 DB 不存在,先跑一次 FeishuAdaptor/AgentConsole 触发建表。

- [ ] **Step 5: 运行 + 手动验证端点**

Run(后台): `dotnet run --project demo/Dashboard`
然后:
```bash
curl -s http://localhost:5080/api/me                              # {"authenticated":false}
curl -s http://localhost:5080/api/sessions                          # 401
curl -s -c cookies.txt -X POST http://localhost:5080/api/login \
  -H "Content-Type: application/json" \
  -d '{"password":"dev-only-test-password"}'                        # {"authenticated":true}
curl -s -b cookies.txt http://localhost:5080/api/sessions           # 200 + 会话 JSON
curl -s -b cookies.txt http://localhost:5080/api/me                 # {"authenticated":true}
```
Expected: 未登录 `/api/sessions` 返回 401;登录后返回会话数组;`/api/me` 登录后 true。验证后停掉进程。

- [ ] **Step 6: Commit**

```bash
git add demo/Dashboard/Program.cs demo/Dashboard/Properties/launchSettings.json
git commit -m "✨ Dashboard 宿主:DI + 只读 DbContext + cookie 鉴权 + API 端点"
```

---

## Task 6: Vite + React 脚手架 + 鉴权门禁

**Files:**
- Create: `demo/Dashboard/client/package.json`
- Create: `demo/Dashboard/client/vite.config.ts`
- Create: `demo/Dashboard/client/tsconfig.json`、`tsconfig.node.json`
- Create: `demo/Dashboard/client/index.html`
- Create: `demo/Dashboard/client/src/main.tsx`、`App.tsx`、`api.ts`、`types.ts`
- Create: `demo/Dashboard/client/src/components/Login.tsx`
- Create: `demo/Dashboard/client/src/styles.css`

**Interfaces:**
- Consumes: Task 5 的 API(`/api/me`、`/api/login`、`/api/logout`)。
- Produces: 可登录的 SPA 外壳(`npm run dev` 起 Vite :5173,proxy `/api` → :5080)。

- [ ] **Step 1: 创建 `demo/Dashboard/client/package.json`**

```json
{
  "name": "mib-dashboard",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "preview": "vite preview"
  },
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "react-markdown": "^9.0.1"
  },
  "devDependencies": {
    "@types/react": "^18.3.12",
    "@types/react-dom": "^18.3.1",
    "@vitejs/plugin-react": "^4.3.4",
    "typescript": "^5.6.3",
    "vite": "^6.0.3"
  }
}
```

- [ ] **Step 2: 创建 `demo/Dashboard/client/vite.config.ts`**

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: { proxy: { '/api': 'http://localhost:5080' } },
  build: { outDir: '../wwwroot', emptyOutDir: true },
})
```

- [ ] **Step 3: 创建 `tsconfig.json` 与 `tsconfig.node.json`**

`demo/Dashboard/client/tsconfig.json`:
```json
{
  "compilerOptions": {
    "target": "ES2022", "useDefineForClassFields": true,
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext", "skipLibCheck": true,
    "moduleResolution": "bundler", "allowImportingTsExtensions": true,
    "resolveJsonModule": true, "isolatedModules": true, "noEmit": true,
    "jsx": "react-jsx", "strict": true
  },
  "include": ["src"],
  "references": [{ "path": "./tsconfig.node.json" }]
}
```
`demo/Dashboard/client/tsconfig.node.json`:
```json
{
  "compilerOptions": {
    "composite": true, "skipLibCheck": true, "module": "ESNext",
    "moduleResolution": "bundler", "allowSyntheticDefaultImports": true
  },
  "include": ["vite.config.ts"]
}
```

- [ ] **Step 4: 创建 `demo/Dashboard/client/index.html`**

```html
<!doctype html>
<html lang="zh">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>ManInBlack Dashboard</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 5: 创建 `src/types.ts`(与 ReadModels 对齐,枚举用 camelCase 字符串)**

```ts
export type MessageBlockKind = 'text' | 'toolCall' | 'toolResult' | 'unknown'

export interface MessageBlock {
  kind: MessageBlockKind
  text?: string; toolName?: string; argumentsJson?: string; resultJson?: string; rawJson?: string
}
export interface MessageView { role: string; blocks: MessageBlock[] }
export interface SessionSummary {
  sessionId: string; messageCount: number; firstAt: string; lastAt: string; userId?: string | null
}
export interface UserSummary { userId: string; metadata: Record<string, unknown>; sessionIds: string[] }
export interface SearchResult { sessionId: string; snippet: string; createdAt: string }
```

- [ ] **Step 6: 创建 `src/api.ts`**

```ts
import type { SessionSummary, UserSummary, MessageView, SearchResult } from './types'

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path, { credentials: 'include' })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return res.json() as Promise<T>
}

export const api = {
  me: () => get<{ authenticated: boolean }>('/api/me'),
  login: (password: string) => fetch('/api/login', {
    method: 'POST', credentials: 'include',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password }),
  }),
  logout: () => fetch('/api/logout', { method: 'POST', credentials: 'include' }),
  sessions: () => get<SessionSummary[]>('/api/sessions'),
  messages: (sessionId: string) =>
    get<MessageView[]>(`/api/sessions/${encodeURIComponent(sessionId)}/messages`),
  users: () => get<UserSummary[]>('/api/users'),
  search: (q: string) => get<SearchResult[]>(`/api/search?q=${encodeURIComponent(q)}`),
}
```

- [ ] **Step 7: 创建 `src/main.tsx` 与 `src/App.tsx`**

`main.tsx`:
```tsx
import { StrictMode } from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import './styles.css'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <StrictMode><App /></StrictMode>,
)
```

`App.tsx`:
```tsx
import { useEffect, useState } from 'react'
import { api } from './api'
import Login from './components/Login'
import Dashboard from './components/Dashboard'

export default function App() {
  const [authed, setAuthed] = useState<boolean | null>(null)
  useEffect(() => { api.me().then(r => setAuthed(r.authenticated)).catch(() => setAuthed(false)) }, [])
  if (authed === null) return <div className="loading">加载中…</div>
  if (!authed) return <Login onLoggedIn={() => setAuthed(true)} />
  return <Dashboard onLogout={async () => { await api.logout(); setAuthed(false) }} />
}
```

- [ ] **Step 8: 创建 `src/components/Login.tsx`**

```tsx
import { useState, type FormEvent } from 'react'
import { api } from '../api'

export default function Login({ onLoggedIn }: { onLoggedIn: () => void }) {
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const res = await api.login(password)
    if (res.ok) onLoggedIn()
    else setError('密码错误')
  }
  return (
    <form className="login" onSubmit={submit}>
      <h1>ManInBlack Dashboard</h1>
      <input type="password" value={password} placeholder="密码"
             onChange={e => setPassword(e.target.value)} />
      <button type="submit">登录</button>
      {error && <div className="error">{error}</div>}
    </form>
  )
}
```

> 注:`src/components/Dashboard.tsx`、`SessionsPanel.tsx`、`UsersPanel.tsx`、`SearchPanel.tsx`、`MessageList.tsx`、`MessageView.tsx` 在 Task 7-8 创建。本步骤先放一个占位 `Dashboard.tsx` 让 `App.tsx` 能编译,Task 7 替换。

**占位 `src/components/Dashboard.tsx`(Task 7 替换):**
```tsx
export default function Dashboard({ onLogout }: { onLogout: () => void }) {
  return <div><button onClick={onLogout}>退出</button><p>会话/消息面板待实现</p></div>
}
```

- [ ] **Step 9: 创建 `src/styles.css`(最小可用样式,Task 7-8 复用)**

```css
* { box-sizing: border-box; }
body { margin: 0; font-family: system-ui, sans-serif; }
.login { max-width: 320px; margin: 10vh auto; display: flex; flex-direction: column; gap: 12px; }
.loading { padding: 2rem; text-align: center; color: #888; }
.error { color: #c00; }
.topbar { display: flex; justify-content: space-between; padding: 8px 16px; background: #1e1e2e; color: #fff; }
.body { display: flex; height: calc(100vh - 48px); }
.sidebar { width: 280px; border-right: 1px solid #ddd; overflow: auto; }
.tabs button { padding: 8px 12px; cursor: pointer; }
.tabs button.active { background: #eef; }
.main { flex: 1; overflow: auto; padding: 16px; }
.session-list, .user-list { list-style: none; margin: 0; padding: 0; }
.session-list li, .user-list > li { padding: 8px 12px; cursor: pointer; border-bottom: 1px solid #eee; }
.session-list li.active, .session-list li:hover { background: #f5f5ff; }
.meta { font-size: 12px; color: #888; }
.message { padding: 8px 0; border-bottom: 1px solid #f0f0f0; }
.message .role { display: inline-block; min-width: 80px; font-weight: 600; color: #555; }
.role-assistant .role { color: #2563eb; }
.role-user .role { color: #16a34a; }
.role-tool .role { color: #9333ea; }
pre { background: #f6f8fa; padding: 8px; overflow: auto; border-radius: 4px; }
.snippet { font-size: 12px; }
```

- [ ] **Step 10: 安装依赖 + 构建验证**

Run:
```bash
cd demo/Dashboard/client && npm install && npm run build
```
Expected: `tsc -b` 无类型错误;`vite build` 产物输出到 `../wwwroot`(`index.html` + `assets/`)。返回仓库根目录。

- [ ] **Step 11: 手动验证登录流程**

两个终端:
- 终端 A: `dotnet run --project demo/Dashboard`(后端 :5080)
- 终端 B: `cd demo/Dashboard/client && npm run dev`(Vite :5173)

浏览器开 `http://localhost:5173`:看到登录页;输错密码显示「密码错误」;输对进入占位 Dashboard(显示「会话/消息面板待实现」+ 退出按钮)。验证后停掉两进程。

- [ ] **Step 12: Commit(不含 wwwroot 产物,已被 gitignore)**

```bash
git add demo/Dashboard/client
git commit -m "✨ Dashboard 前端脚手架:Vite + React + 登录门禁"
```

---

## Task 7: 会话列表 + 消息查看组件

**Files:**
- Create: `demo/Dashboard/client/src/components/SessionsPanel.tsx`
- Create: `demo/Dashboard/client/src/components/MessageList.tsx`
- Create: `demo/Dashboard/client/src/components/MessageView.tsx`
- Modify: `demo/Dashboard/client/src/components/Dashboard.tsx`(替换 Task 6 占位)

**Interfaces:**
- Consumes: `api.sessions()`、`api.messages(id)`(Task 6);`MessageView`/`SessionSummary` 类型。
- Produces: 完整 Dashboard 布局:左 sidebar「会话」tab,右主消息区。

- [ ] **Step 1: 创建 `SessionsPanel.tsx`**

```tsx
import { useEffect, useState } from 'react'
import { api } from '../api'
import type { SessionSummary } from '../types'

export default function SessionsPanel({ activeSession, onSelect }: {
  activeSession: string | null; onSelect: (s: string) => void
}) {
  const [sessions, setSessions] = useState<SessionSummary[]>([])
  const [error, setError] = useState('')
  useEffect(() => { api.sessions().then(setSessions).catch(e => setError(String(e))) }, [])
  if (error) return <div className="error">{error}</div>
  return (
    <ul className="session-list">
      {sessions.map(s => (
        <li key={s.sessionId} className={s.sessionId === activeSession ? 'active' : ''}
            onClick={() => onSelect(s.sessionId)}>
          <div>{s.sessionId.slice(0, 12)}</div>
          <div className="meta">{s.messageCount} 条 · {s.lastAt}</div>
        </li>
      ))}
    </ul>
  )
}
```

- [ ] **Step 2: 创建 `MessageView.tsx`(注意组件名与类型名冲突,类型别名 MV)**

```tsx
import ReactMarkdown from 'react-markdown'
import type { MessageView as MV } from '../types'

export default function MessageView({ message }: { message: MV }) {
  return (
    <div className={`message role-${message.role}`}>
      <span className="role">{message.role}</span>
      <div className="blocks">
        {message.blocks.map((b, i) => {
          switch (b.kind) {
            case 'text': return <ReactMarkdown key={i}>{b.text ?? ''}</ReactMarkdown>
            case 'toolCall': return (
              <details key={i}><summary>▸ 工具调用 {b.toolName}</summary><pre>{b.argumentsJson}</pre></details>)
            case 'toolResult': return (
              <details key={i}><summary>▸ 工具结果</summary><pre>{b.resultJson}</pre></details>)
            default: return (
              <details key={i}><summary>▸ 未知内容</summary><pre>{b.rawJson}</pre></details>)
          }
        })}
      </div>
    </div>
  )
}
```

- [ ] **Step 3: 创建 `MessageList.tsx`**

```tsx
import { useEffect, useState } from 'react'
import { api } from '../api'
import type { MessageView } from '../types'
import MessageViewComp from './MessageView'

export default function MessageList({ sessionId }: { sessionId: string }) {
  const [messages, setMessages] = useState<MessageView[]>([])
  const [error, setError] = useState('')
  useEffect(() => {
    setError(''); setMessages([])
    api.messages(sessionId).then(setMessages).catch(e => setError(String(e)))
  }, [sessionId])
  if (error) return <div className="error">无法加载:{error}</div>
  return <div>{messages.map((m, i) => <MessageViewComp key={i} message={m} />)}</div>
}
```

- [ ] **Step 4: 替换 `Dashboard.tsx`(布局:顶栏 + sidebar 三 tab + 主区)**

```tsx
import { useState } from 'react'
import SessionsPanel from './SessionsPanel'
import UsersPanel from './UsersPanel'
import SearchPanel from './SearchPanel'
import MessageList from './MessageList'

type Tab = 'sessions' | 'users' | 'search'

export default function Dashboard({ onLogout }: { onLogout: () => void }) {
  const [tab, setTab] = useState<Tab>('sessions')
  const [activeSession, setActiveSession] = useState<string | null>(null)
  return (
    <div>
      <header className="topbar">
        <span>ManInBlack Dashboard</span>
        <button onClick={onLogout}>退出</button>
      </header>
      <div className="body">
        <aside className="sidebar">
          <nav className="tabs">
            <button className={tab === 'sessions' ? 'active' : ''} onClick={() => setTab('sessions')}>会话</button>
            <button className={tab === 'users' ? 'active' : ''} onClick={() => setTab('users')}>用户</button>
            <button className={tab === 'search' ? 'active' : ''} onClick={() => setTab('search')}>搜索</button>
          </nav>
          {tab === 'sessions' && <SessionsPanel activeSession={activeSession} onSelect={setActiveSession} />}
          {tab === 'users' && <UsersPanel onSelect={s => { setActiveSession(s); setTab('sessions') }} />}
          {tab === 'search' && <SearchPanel onSelect={setActiveSession} />}
        </aside>
        <main className="main">
          {activeSession ? <MessageList sessionId={activeSession} /> : <div className="loading">选择一个会话</div>}
        </main>
      </div>
    </div>
  )
}
```

> 注:`UsersPanel`/`SearchPanel` 在 Task 8 创建。本步骤先放占位让编译通过,Task 8 替换。

**占位 `UsersPanel.tsx` 与 `SearchPanel.tsx`(Task 8 替换):**
```tsx
// UsersPanel.tsx
export default function UsersPanel(_: { onSelect: (s: string) => void }) {
  return <div className="loading">用户面板待实现</div>
}
```
```tsx
// SearchPanel.tsx
export default function SearchPanel(_: { onSelect: (s: string) => void }) {
  return <div className="loading">搜索面板待实现</div>
}
```

- [ ] **Step 5: 构建 + 手动验证**

Run: `cd demo/Dashboard/client && npm run build`(确认无 TS 错误)
双进程启动(Task 6 Step 11 方式),登录后在「会话」tab 选一个会话,主区看到消息列表(role 徽章 + 文本 markdown + 工具调用/结果可折叠卡片)。

- [ ] **Step 6: Commit**

```bash
git add demo/Dashboard/client/src/components/SessionsPanel.tsx demo/Dashboard/client/src/components/MessageList.tsx demo/Dashboard/client/src/components/MessageView.tsx demo/Dashboard/client/src/components/Dashboard.tsx demo/Dashboard/client/src/components/UsersPanel.tsx demo/Dashboard/client/src/components/SearchPanel.tsx
git commit -m "✨ Dashboard 会话列表 + 消息查看组件"
```

---

## Task 8: 用户视图 + 搜索面板

**Files:**
- Modify: `demo/Dashboard/client/src/components/UsersPanel.tsx`(替换占位)
- Modify: `demo/Dashboard/client/src/components/SearchPanel.tsx`(替换占位)

**Interfaces:**
- Consumes: `api.users()`、`api.search(q)`;`UserSummary`/`SearchResult` 类型。
- Produces: sidebar「用户」「搜索」两个 tab。

- [ ] **Step 1: 替换 `UsersPanel.tsx`**

```tsx
import { useEffect, useState } from 'react'
import { api } from '../api'
import type { UserSummary } from '../types'

export default function UsersPanel({ onSelect }: { onSelect: (s: string) => void }) {
  const [users, setUsers] = useState<UserSummary[]>([])
  const [error, setError] = useState('')
  useEffect(() => { api.users().then(setUsers).catch(e => setError(String(e))) }, [])
  if (error) return <div className="error">{error}</div>
  return (
    <ul className="user-list">
      {users.map(u => (
        <li key={u.userId}>
          <div>{u.userId} <span className="meta">({u.sessionIds.length})</span></div>
          <ul className="session-list">
            {u.sessionIds.map(s => (
              <li key={s} onClick={() => onSelect(s)}>
                <div>{s.slice(0, 12)}</div>
              </li>
            ))}
          </ul>
        </li>
      ))}
    </ul>
  )
}
```

- [ ] **Step 2: 替换 `SearchPanel.tsx`**

```tsx
import { useState, type FormEvent } from 'react'
import { api } from '../api'
import type { SearchResult } from '../types'

export default function SearchPanel({ onSelect }: { onSelect: (s: string) => void }) {
  const [q, setQ] = useState('')
  const [results, setResults] = useState<SearchResult[]>([])
  const [error, setError] = useState('')
  const search = async (e: FormEvent) => {
    e.preventDefault()
    if (!q.trim()) return
    setError('')
    try { setResults(await api.search(q)) } catch (ex) { setError(String(ex)) }
  }
  return (
    <div>
      <form onSubmit={search} style={{ display: 'flex', gap: 4, padding: 8 }}>
        <input value={q} onChange={e => setQ(e.target.value)} placeholder="搜索内容…" />
        <button>搜</button>
      </form>
      {error && <div className="error">{error}</div>}
      <ul className="session-list">
        {results.map((r, i) => (
          <li key={i} onClick={() => onSelect(r.sessionId)}>
            <div>{r.sessionId.slice(0, 12)} <span className="meta">· {r.createdAt}</span></div>
            <pre className="snippet">{r.snippet}</pre>
          </li>
        ))}
      </ul>
    </div>
  )
}
```

- [ ] **Step 3: 构建 + 手动验证**

Run: `cd demo/Dashboard/client && npm run build`
双进程启动,验证「用户」tab 列出用户与会话、「搜索」tab 输入关键词后出现命中片段,点击任一会话跳到主区显示消息。

- [ ] **Step 4: Commit**

```bash
git add demo/Dashboard/client/src/components/UsersPanel.tsx demo/Dashboard/client/src/components/SearchPanel.tsx
git commit -m "✨ Dashboard 用户视图 + 全文搜索面板"
```

---

## Task 9: 构建集成(MSBuild publish target)+ dotnet publish 验证

**Files:**
- Verify: `demo/Dashboard/Dashboard.csproj`(Task 1 已含 `BuildClient` target)
- Create: `demo/Dashboard/client/.gitignore`

**Interfaces:**
- Produces: `dotnet publish demo/Dashboard -c Release` 自动构建前端并产出含 wwwroot 的发布目录。

- [ ] **Step 1: 确认 csproj 已含 target**

打开 `demo/Dashboard/Dashboard.csproj`,确认存在(Task 1 创建):
```xml
<Target Name="BuildClient" BeforeTargets="Publish">
  <Exec Command="npm ci" WorkingDirectory="client" Condition="Exists('client/package-lock.json')" />
  <Exec Command="npm install" WorkingDirectory="client" Condition="!Exists('client/package-lock.json')" />
  <Exec Command="npm run build" WorkingDirectory="client" />
</Target>
```

- [ ] **Step 2: 创建 `demo/Dashboard/client/.gitignore`(忽略 node_modules)**

```
node_modules
```

- [ ] **Step 3: 生成 package-lock.json**

Run: `cd demo/Dashboard/client && npm install`
Expected: 生成 `package-lock.json`(供 `npm ci` 用)。

- [ ] **Step 4: 验证 publish**

Run:
```bash
dotnet publish demo/Dashboard -c Release -o ./publish-test
```
Expected: 输出含 `BuildClient` 执行日志(`npm ci` + `npm run build`),`./publish-test/wwwroot/` 含 `index.html` + `assets/`。验证后 `rm -rf ./publish-test`。

- [ ] **Step 5: Commit**

```bash
git add demo/Dashboard/client/.gitignore demo/Dashboard/client/package-lock.json
git commit -m "🔧 Dashboard publish 集成:MSBuild 触发 npm 构建"
```

---

## Task 10: 文档 + 收尾

**Files:**
- Create: `docs/dashboard-guide.md`
- Modify: `CLAUDE.md`(加构建/运行命令 + 文档索引)
- Modify: `docs/storage-guide.md`(加一行指向 Dashboard)

- [ ] **Step 1: 创建 `docs/dashboard-guide.md`**

```markdown
# Dashboard 指南

ManInBlack Dashboard 是一个**只读**的 Web 应用,用于浏览器查看 SQLite 中的会话消息、用户,并支持全文搜索。团队部署常驻。

## 配置

`~/.man-in-black/settings.json` 新增节(密码必填,缺省则**拒绝启动**):

​```json
"Dashboard": { "Password": "a-long-random-shared-secret" }
​```

Dashboard 直读同一个 `maninblack.db`(只读连接,WAL 下与 FeishuAdaptor 并发安全),不建表、不迁移。

## 开发

​```bash
# 后端 API(:5080)
dotnet run --project demo/Dashboard
# 前端 Vite(:5173,proxy /api → :5080)
cd demo/Dashboard/client && npm run dev
​```

浏览器访问 `http://localhost:5173`。

## 发布与部署

​```bash
dotnet publish demo/Dashboard -c Release -o ./publish
​```

`dotnet publish` 经 MSBuild target 自动执行 `npm ci && npm run build`,产物落到 `wwwroot/`。**发布机需 Node**,运行时仅 .NET + 静态文件。

部署沿用 FeishuAdaptor 模式:`publish linux-x64` + systemd + 反向代理;应用层密码之外可在反向代理叠一层 basic auth。

## 安全

- cookie 鉴权,密码固定时长比对(防计时侧信道);fail-closed。
- 工具调用/结果 JSON 走 `JSON.stringify` 插入,React 默认转义,无 XSS 注入面;文本块经 react-markdown 渲染。
- 严格只读:连接串 `Mode=ReadOnly`,无任何写端点。
```

- [ ] **Step 2: 更新 `CLAUDE.md`**

在「构建与测试」代码块追加:
```bash
dotnet run --project demo/Dashboard                            # Dashboard API(:5080)
cd demo/Dashboard/client && npm run dev                        # Dashboard 前端(:5173)
dotnet publish demo/Dashboard -c Release                       # 发布(含前端构建)
dotnet test test/Dashboard.Tests                               # Dashboard 测试
```
在「文档索引」列表追加:
```markdown
- [Dashboard 指南](docs/dashboard-guide.md)
```

- [ ] **Step 3: 更新 `docs/storage-guide.md`**

在「概述」节末尾追加一行:
```markdown
> 可用 [Dashboard](dashboard-guide.md) demo 在浏览器查看库内会话消息与用户。
```

- [ ] **Step 4: 全量构建 + 测试**

Run:
```bash
dotnet build ManInBlack.slnx
dotnet test test/Dashboard.Tests
```
Expected: 解决方案 BUILD SUCCEEDED;Dashboard.Tests 全绿。

- [ ] **Step 5: Commit**

```bash
git add docs/dashboard-guide.md CLAUDE.md docs/storage-guide.md
git commit -m "📝 Dashboard 指南 + 索引更新"
```

---

## 完成标准

- `dotnet build ManInBlack.slnx` 成功;`dotnet test test/Dashboard.Tests` 全绿。
- `dotnet publish demo/Dashboard -c Release` 产出含 `wwwroot` 的发布目录。
- 双进程启动后:登录 → 会话列表 → 消息查看(文本/工具调用/结果/未知块)→ 用户视图 → 搜索,均正常。
- 缺省 `Dashboard:Password` 时启动抛异常(fail-closed)。
- 文档与 CLAUDE.md 索引同步更新。
