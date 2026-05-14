# Agent 状态持久化与 Session 恢复 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 扩展现有持久化机制，支持 Agent 核心状态（Items + SystemPrompt）的快照保存与恢复，实现崩溃恢复和长任务断点续传。

**Architecture:** 合并 `ISessionStorage` 为 `IAgentStateStorage`，新增快照能力。在 `AgentLoopMiddleware` 每轮工具调用后通过 `Items["SaveCheckpoint"]` 回调触发检查点保存。`ReadPersistenceMiddleware` 恢复时自动检测快照并还原状态。

**Tech Stack:** .NET 10, System.Text.Json, xunit, 手写 fake

---

## 文件结构

| 操作 | 文件路径 | 职责 |
|------|---------|------|
| 新建 | `src/ManInBlack.AI.Abstraction/Storage/AgentStateSnapshot.cs` | 快照数据模型 |
| 修改 | `src/ManInBlack.AI.Abstraction/Storage/ISessionStorage.cs` | 新增 `IAgentStateStorage` 接口 + `ICheckpointPolicy` |
| 修改 | `src/ManInBlack.AI/Services/FileSessionStorage.cs` | 扩展为 `FileAgentStateStorage`，实现快照读写 |
| 修改 | `src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs` | 恢复逻辑 + 注入 SaveCheckpoint 回调 |
| 修改 | `src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs` | 每轮工具调用后触发检查点 |
| 修改 | `src/ManInBlack.AI/DependencyInjection.cs` | 注册新服务 |
| 修改 | `test/ManInBlack.AI.Tests/Helpers/FakeStorage.cs` | 新增 `FakeAgentStateStorage` |
| 新建 | `test/ManInBlack.AI.Tests/Middlewares/CheckpointTests.cs` | 快照相关测试 |
| 修改 | `test/ManInBlack.AI.Tests/Middlewares/PersistenceMiddlewareTests.cs` | 适配 `IAgentStateStorage` |

---

### Task 1: 定义 IAgentStateStorage 接口和 AgentStateSnapshot 模型

**Files:**
- Modify: `src/ManInBlack.AI.Abstraction/Storage/ISessionStorage.cs`
- Create: `src/ManInBlack.AI.Abstraction/Storage/AgentStateSnapshot.cs`

- [ ] **Step 1: 创建 AgentStateSnapshot 数据模型**

创建 `src/ManInBlack.AI.Abstraction/Storage/AgentStateSnapshot.cs`：

```csharp
namespace ManInBlack.AI.Abstraction.Storage;

/// <summary>
/// Agent 状态快照，用于崩溃恢复和断点续传
/// </summary>
public sealed record AgentStateSnapshot
{
    public string SessionId { get; init; } = "";
    public string AgentName { get; init; } = "";
    public string SystemPrompt { get; init; } = "";
    public Dictionary<string, object> Items { get; init; } = [];
    public DateTimeOffset SavedAt { get; init; }
    /// <summary>
    /// 检查点原因："ToolCallCompleted" 或 "SessionEnd"
    /// </summary>
    public string? CheckpointReason { get; init; }
}
```

- [ ] **Step 2: 在 ISessionStorage.cs 中新增 IAgentStateStorage 和 ICheckpointPolicy**

在 `src/ManInBlack.AI.Abstraction/Storage/ISessionStorage.cs` 文件末尾（`UserEntryExtensions` 之后）添加：

```csharp
/// <summary>
/// Agent 状态存储接口，合并消息持久化和状态快照能力
/// </summary>
public interface IAgentStateStorage : ISessionStorage
{
    /// <summary>
    /// 加载状态快照，无快照时返回 null
    /// </summary>
    Task<AgentStateSnapshot?> LoadSnapshotAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// 保存状态快照
    /// </summary>
    Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// 删除状态快照
    /// </summary>
    Task DeleteSnapshotAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>
/// 检查点保存策略，控制何时触发快照保存
/// </summary>
public interface ICheckpointPolicy
{
    /// <summary>
    /// 判断是否应该保存检查点
    /// </summary>
    /// <param name="phase">阶段标识："AfterToolCall" 或 "SessionEnd"</param>
    bool ShouldSave(string phase);
}
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/ManInBlack.AI.Abstraction`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: 提交**

```bash
git add src/ManInBlack.AI.Abstraction/Storage/AgentStateSnapshot.cs src/ManInBlack.AI.Abstraction/Storage/ISessionStorage.cs
git commit -m "✨ 新增 IAgentStateStorage 接口和 AgentStateSnapshot 模型"
```

---

### Task 2: 实现 FileAgentStateStorage

**Files:**
- Modify: `src/ManInBlack.AI/Services/FileSessionStorage.cs`

- [ ] **Step 1: 改造 FileSessionStorage 实现 IAgentStateStorage**

将 `src/ManInBlack.AI/Services/FileSessionStorage.cs` 替换为以下内容（保持原有消息逻辑不变，新增快照读写）：

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services;

[ServiceRegister.Singleton.As<ISessionStorage>]
public class FileAgentStateStorage(IOptions<AgentStorageOptions> options, ILogger<FileAgentStateStorage> logger)
    : IAgentStateStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AgentStorageOptions _options = options.Value;

    private string SessionDir => Path.Combine(_options.RootPath, "sessions");

    /// <inheritdoc/>
    public async Task SaveMessage(string sessionId, ChatMessage message)
    {
        Directory.CreateDirectory(SessionDir);
        var sessionFile = Path.Combine(SessionDir, $"{sessionId}.jsonl");
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await File.AppendAllTextAsync(sessionFile, json + Environment.NewLine);
    }

    /// <inheritdoc/>
    public async Task<IList<ChatMessage>> LoadMessages(string sessionId)
    {
        Directory.CreateDirectory(SessionDir);
        var messages = new List<ChatMessage>();
        var sessionFile = Path.Combine(SessionDir, $"{sessionId}.jsonl");

        logger.LogInformation("Loading session {SessionId} from file {SessionFile}", sessionId, sessionFile);

        if (!File.Exists(sessionFile))
        {
            await File.Create(sessionFile).DisposeAsync();
            return messages;
        }

        await foreach (var line in File.ReadLinesAsync(sessionFile))
        {
            var message = JsonSerializer.Deserialize<ChatMessage>(line, JsonOptions);
            if (message != null)
                messages.Add(message);
        }

        return messages;
    }

    /// <inheritdoc/>
    public async Task<AgentStateSnapshot?> LoadSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        var snapshotFile = Path.Combine(SessionDir, $"{sessionId}.state.json");
        if (!File.Exists(snapshotFile))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(snapshotFile, ct);
            return JsonSerializer.Deserialize<AgentStateSnapshot>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "快照文件损坏，将忽略: {File}", snapshotFile);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(SessionDir);
        var snapshotFile = Path.Combine(SessionDir, $"{sessionId}.state.json");
        var tempFile = snapshotFile + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(tempFile, json, ct);
            File.Move(tempFile, snapshotFile, overwrite: true);
        }
        catch
        {
            // 清理临时文件
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task DeleteSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        var snapshotFile = Path.Combine(SessionDir, $"{sessionId}.state.json");
        if (File.Exists(snapshotFile))
            File.Delete(snapshotFile);
        return Task.CompletedTask;
    }
}
```

注意：类名从 `FileSessionStorage` 改为 `FileAgentStateStorage`，`[ServiceRegister.Singleton.As<ISessionStorage>]` 属性保持不变（同时实现 `IAgentStateStorage`，通过 DI 注册为 `ISessionStorage`）。需要在 `DependencyInjection.cs` 中额外注册 `IAgentStateStorage`。

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/ManInBlack.AI`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: 提交**

```bash
git add src/ManInBlack.AI/Services/FileSessionStorage.cs
git commit -m "✨ FileAgentStateStorage 实现快照读写"
```

---

### Task 3: 注册 DI 服务

**Files:**
- Modify: `src/ManInBlack.AI/DependencyInjection.cs`

- [ ] **Step 1: 在 AddManInBlack 方法中注册 IAgentStateStorage 和 ICheckpointPolicy**

在 `src/ManInBlack.AI/DependencyInjection.cs` 的 `AddManInBlack` 方法中，`services.AddAutoRegisteredServices();` 之前添加：

```csharp
services.TryAddSingleton<IAgentStateStorage>(
    sp => (IAgentStateStorage)sp.GetRequiredService<ISessionStorage>());
services.TryAddSingleton<ICheckpointPolicy, AfterToolCallPolicy>();
```

在文件顶部添加 using：

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
```

- [ ] **Step 2: 创建 AfterToolCallPolicy 默认实现**

在 `src/ManInBlack.AI/Services/` 下新建 `AfterToolCallPolicy.cs`：

```csharp
using ManInBlack.AI.Abstraction.Storage;

namespace ManInBlack.AI.Services;

/// <summary>
/// 默认检查点策略：每轮工具调用后和 session 结束时都保存
/// </summary>
public class AfterToolCallPolicy : ICheckpointPolicy
{
    public bool ShouldSave(string phase) => phase is "AfterToolCall" or "SessionEnd";
}
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/ManInBlack.AI`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: 提交**

```bash
git add src/ManInBlack.AI/DependencyInjection.cs src/ManInBlack.AI/Services/AfterToolCallPolicy.cs
git commit -m "✨ 注册 IAgentStateStorage 和 ICheckpointPolicy DI 服务"
```

---

### Task 4: FakeAgentStateStorage 测试基础设施

**Files:**
- Modify: `test/ManInBlack.AI.Tests/Helpers/FakeStorage.cs`

- [ ] **Step 1: 新增 FakeAgentStateStorage**

在 `test/ManInBlack.AI.Tests/Helpers/FakeStorage.cs` 文件末尾添加：

```csharp
/// <summary>
/// 内存版 IAgentStateStorage，用 Dictionary 代替文件 I/O
/// </summary>
public class FakeAgentStateStorage : IAgentStateStorage
{
    private readonly Dictionary<string, List<ChatMessage>> _messages = new();
    private readonly Dictionary<string, AgentStateSnapshot> _snapshots = new();

    public Task SaveMessage(string sessionId, ChatMessage message)
    {
        if (!_messages.TryGetValue(sessionId, out var list))
        {
            list = [];
            _messages[sessionId] = list;
        }
        list.Add(message);
        return Task.CompletedTask;
    }

    public Task<IList<ChatMessage>> LoadMessages(string sessionId)
    {
        if (_messages.TryGetValue(sessionId, out var list))
            return Task.FromResult<IList<ChatMessage>>([.. list]);
        return Task.FromResult<IList<ChatMessage>>([]);
    }

    public Task<AgentStateSnapshot?> LoadSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        _snapshots.TryGetValue(sessionId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot, CancellationToken ct = default)
    {
        _snapshots[sessionId] = snapshot;
        return Task.CompletedTask;
    }

    public Task DeleteSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        _snapshots.Remove(sessionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取所有快照，用于断言
    /// </summary>
    public IReadOnlyDictionary<string, AgentStateSnapshot> AllSnapshots => _snapshots;
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build test/ManInBlack.AI.Tests`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: 提交**

```bash
git add test/ManInBlack.AI.Tests/Helpers/FakeStorage.cs
git commit -m "✨ 新增 FakeAgentStateStorage 测试基础设施"
```

---

### Task 5: 适配现有测试为 IAgentStateStorage

**Files:**
- Modify: `test/ManInBlack.AI.Tests/Middlewares/PersistenceMiddlewareTests.cs`

- [ ] **Step 1: 将所有 FakeSessionStorage 替换为 FakeAgentStateStorage**

在 `test/ManInBlack.AI.Tests/Middlewares/PersistenceMiddlewareTests.cs` 中：

1. 将所有 `new FakeSessionStorage()` 替换为 `(ISessionStorage)new FakeAgentStateStorage()`
2. 将所有 `.AddSingleton<ISessionStorage>(storage)` 中的类型保持不变（因为 `FakeAgentStateStorage` 实现了 `IAgentStateStorage` 即 `ISessionStorage`）
3. 更新 `ReadPersistenceMiddleware` 测试中需要 `IUserStorage` 的地方，确保 DI 容器也注册了 `IUserStorage`

具体修改——将 `ReadPersistenceMiddlewareTests` 和 `SavePersistenceMiddlewareTests` 中所有 `new FakeSessionStorage()` 替换为 `new FakeAgentStateStorage()`，变量类型改为 `var`：

```csharp
// 之前
var storage = new FakeSessionStorage();

// 之后
var storage = new FakeAgentStateStorage();
```

同时将 `AddSingleton<ISessionStorage>(storage)` 改为 `AddSingleton<IAgentStateStorage>(storage)` 和 `AddSingleton<ISessionStorage>(storage)` 两行（因为中间件注入的是 `ISessionStorage`）：

```csharp
// 之前
.AddSingleton<ISessionStorage>(storage)

// 之后
.AddSingleton<IAgentStateStorage>(storage)
.AddSingleton<ISessionStorage>(storage)
```

- [ ] **Step 2: 运行现有测试验证无回归**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~PersistenceMiddleware"`
Expected: ALL PASSED

- [ ] **Step 3: 提交**

```bash
git add test/ManInBlack.AI.Tests/Middlewares/PersistenceMiddlewareTests.cs
git commit -m "🧪 适配现有持久化测试为 IAgentStateStorage"
```

---

### Task 6: 扩展 ReadPersistenceMiddleware — 恢复逻辑

**Files:**
- Modify: `src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs`

- [ ] **Step 1: 在 ReadPersistenceMiddleware 中添加快照恢复和 SaveCheckpoint 注入**

修改 `src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs` 中的 `ReadPersistenceMiddleware.HandleAsync` 方法。

在 `HandleAsync` 方法开头（`var sessionStorage = ...` 之后、命令检查之前）添加快照恢复和回调注入逻辑：

```csharp
public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
    AgentContext context,
    ChatResponseUpdateHandler next,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var sessionStorage = context.ServiceProvider.GetRequiredService<ISessionStorage>();

    // 恢复状态快照
    if (sessionStorage is IAgentStateStorage stateStorage)
    {
        var snapshot = await stateStorage.LoadSnapshotAsync(context.SessionId, ct);
        if (snapshot is not null)
        {
            context.SystemPrompt = snapshot.SystemPrompt;
            foreach (var (key, value) in snapshot.Items)
                context.Items[key] = value;
        }
    }

    // 注入 SaveCheckpoint 回调
    context.Items["SaveCheckpoint"] = (Func<string?, CancellationToken, Task>)(async (reason, token) =>
    {
        if (sessionStorage is not IAgentStateStorage stateStorage)
            return;
        var policy = context.ServiceProvider.GetService(typeof(ICheckpointPolicy)) as ICheckpointPolicy;
        if (policy is not null && !policy.ShouldSave(reason ?? "Unknown"))
            return;
        var snapshot = new AgentStateSnapshot
        {
            SessionId = context.SessionId,
            AgentName = context.AgentName,
            SystemPrompt = context.SystemPrompt,
            Items = SerializeItems(context.Items),
            SavedAt = DateTimeOffset.UtcNow,
            CheckpointReason = reason,
        };
        try
        {
            await stateStorage.SaveSnapshotAsync(context.SessionId, snapshot, token);
        }
        catch (Exception ex)
        {
            // 保存失败不影响对话
            var logger = context.ServiceProvider.GetService<ILogger<ReadPersistenceMiddleware>>();
            logger?.LogWarning(ex, "保存检查点失败: {SessionId}", context.SessionId);
        }
    });

    // ... 后续原有逻辑（命令检查、加载消息、执行管道）保持不变
```

在文件底部（`SavePersistenceMiddleware` 类之前或 `namespace` 内）添加静态辅助方法：

```csharp
file static class PersistenceHelper
{
    public static Dictionary<string, object> SerializeItems(IDictionary<string, object> items)
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, value) in items)
        {
            if (key == "SaveCheckpoint") continue;
            try
            {
                System.Text.Json.JsonSerializer.SerializeToElement(value);
                result[key] = value;
            }
            catch
            {
                // 不可序列化的值跳过
            }
        }
        return result;
    }
}
```

需要在文件顶部添加 using：

```csharp
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.Logging;
```

- [ ] **Step 2: 在 SavePersistenceMiddleware 结束时触发 SessionEnd 检查点**

修改 `SavePersistenceMiddleware.HandleAsync` 方法，在 `FlushAsync` 之后触发 SessionEnd 检查点：

```csharp
context.Messages = original;
await persisting.FlushAsync();

// session 结束时保存最终检查点
if (context.Items.TryGetValue("SaveCheckpoint", out var obj) && obj is Func<string?, CancellationToken, Task> save)
{
    await save("SessionEnd", ct);
}
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/ManInBlack.AI`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: 运行现有持久化测试验证无回归**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~PersistenceMiddleware"`
Expected: ALL PASSED

- [ ] **Step 5: 提交**

```bash
git add src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs
git commit -m "✨ ReadPersistenceMiddleware 恢复快照 + 注入 SaveCheckpoint 回调"
```

---

### Task 7: 扩展 AgentLoopMiddleware — 触发检查点

**Files:**
- Modify: `src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs`

- [ ] **Step 1: 在 AllToolsCompleted 事件发布后触发检查点**

在 `src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs` 的 `while` 循环中，在 `AllToolsCompletedEvent` 发布之后（第 110 行之后）、循环继续之前，添加检查点触发逻辑：

```csharp
// ── AllToolsCompleted：本批次所有工具执行完毕后触发 ──
await bus.PublishAsync(key, new AllToolsCompletedEvent
{
    AgentId = key,
}, ct);

// ── 检查点保存 ──
if (context.Items.TryGetValue("SaveCheckpoint", out var obj) && obj is Func<string?, CancellationToken, Task> save)
{
    await save("AfterToolCall", ct);
}
```

- [ ] **Step 2: 在无工具调用退出时也触发 SessionEnd 检查点（可选，中间件管道层已有）**

无额外修改——`SavePersistenceMiddleware` 已在 session 结束时触发 `SessionEnd`。`AgentLoopMiddleware` 只需在工具调用后触发。

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/ManInBlack.AI`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: 提交**

```bash
git add src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs
git commit -m "✨ AgentLoopMiddleware 工具调用后触发检查点"
```

---

### Task 8: 快照保存与加载测试

**Files:**
- Create: `test/ManInBlack.AI.Tests/Middlewares/CheckpointTests.cs`

- [ ] **Step 1: 编写测试**

创建 `test/ManInBlack.AI.Tests/Middlewares/CheckpointTests.cs`：

```csharp
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Services;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class CheckpointTests
{
    /// <summary>
    /// 快照保存后加载，Items 和 SystemPrompt 应一致
    /// </summary>
    [Fact]
    public async Task SaveAndLoadSnapshot_ShouldRestoreState()
    {
        var storage = new FakeAgentStateStorage();
        var snapshot = new AgentStateSnapshot
        {
            SessionId = "s1",
            AgentName = "TestAgent",
            SystemPrompt = "test prompt",
            Items = new Dictionary<string, object> { ["key1"] = "value1" },
            SavedAt = DateTimeOffset.UtcNow,
            CheckpointReason = "ToolCallCompleted"
        };

        await storage.SaveSnapshotAsync("s1", snapshot);
        var loaded = await storage.LoadSnapshotAsync("s1");

        Assert.NotNull(loaded);
        Assert.Equal("s1", loaded.SessionId);
        Assert.Equal("TestAgent", loaded.AgentName);
        Assert.Equal("test prompt", loaded.SystemPrompt);
        Assert.Equal("value1", loaded.Items["key1"]);
        Assert.Equal("ToolCallCompleted", loaded.CheckpointReason);
    }

    /// <summary>
    /// 无快照时应返回 null
    /// </summary>
    [Fact]
    public async Task LoadSnapshot_NoSnapshot_ShouldReturnNull()
    {
        var storage = new FakeAgentStateStorage();
        var result = await storage.LoadSnapshotAsync("nonexistent");
        Assert.Null(result);
    }

    /// <summary>
    /// ReadPersistenceMiddleware 恢复快照时还原 Items 和 SystemPrompt
    /// </summary>
    [Fact]
    public async Task ReadPersistence_ShouldRestoreSnapshot()
    {
        var storage = new FakeAgentStateStorage();
        await storage.SaveSnapshotAsync("s1", new AgentStateSnapshot
        {
            SessionId = "s1",
            AgentName = "Agent",
            SystemPrompt = "restored prompt",
            Items = new Dictionary<string, object> { ["myKey"] = "myValue" },
        });

        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "hello",
            SystemPrompt = "original prompt",
            Messages = [new(ChatRole.User, "hello")]
        };

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Equal("restored prompt", ctx.SystemPrompt);
        Assert.Equal("myValue", ctx.Items["myKey"]);
    }

    /// <summary>
    /// Items 中的不可序列化值在保存时被跳过
    /// </summary>
    [Fact]
    public async Task SaveCheckpoint_ShouldSkipNonSerializableItems()
    {
        var storage = new FakeAgentStateStorage();
        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();

        var middleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "hello",
            Messages = [new(ChatRole.User, "hello")]
        };

        // 执行中间件以注入 SaveCheckpoint 回调
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 添加不可序列化对象
        ctx.Items["func"] = (Func<int>)(() => 42);
        ctx.Items["valid"] = "hello";

        // 手动触发保存
        if (ctx.Items.TryGetValue("SaveCheckpoint", out var obj) && obj is Func<string?, CancellationToken, Task> save)
            await save("SessionEnd", CancellationToken.None);

        var snapshot = await storage.LoadSnapshotAsync("s1");
        Assert.NotNull(snapshot);
        Assert.False(snapshot.Items.ContainsKey("func"));
        Assert.False(snapshot.Items.ContainsKey("SaveCheckpoint"));
        Assert.Equal("hello", snapshot.Items["valid"]);
    }

    /// <summary>
    /// SavePersistenceMiddleware 结束时应触发 SessionEnd 检查点
    /// </summary>
    [Fact]
    public async Task SavePersistence_ShouldTriggerSessionEndCheckpoint()
    {
        var storage = new FakeAgentStateStorage();
        var services = new ServiceCollection()
            .AddSingleton<IAgentStateStorage>(storage)
            .AddSingleton<ISessionStorage>(storage)
            .AddSingleton<IUserStorage>(new FakeUserStorage())
            .BuildServiceProvider();

        // 先用 ReadPersistenceMiddleware 注入 SaveCheckpoint 回调
        var readMiddleware = new ReadPersistenceMiddleware();
        var ctx = new AgentContext(services)
        {
            SessionId = "s1",
            ParentId = "u1",
            UserInput = "hello",
            SystemPrompt = "test prompt",
            Messages = [new(ChatRole.User, "hello")]
        };

        await readMiddleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        // 再用 SavePersistenceMiddleware 执行
        var saveMiddleware = new SavePersistenceMiddleware();
        ChatResponseUpdateHandler next = () =>
        {
            ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, "response"));
            return TestHelpers.EmptyStream;
        };

        await saveMiddleware.HandleAsync(ctx, next).ToListAsync();

        var snapshot = await storage.LoadSnapshotAsync("s1");
        Assert.NotNull(snapshot);
        Assert.Equal("SessionEnd", snapshot.CheckpointReason);
    }

    /// <summary>
    /// 损坏快照应返回 null 且不抛异常
    /// </summary>
    [Fact]
    public async Task LoadSnapshot_CorruptedJson_ShouldReturnNull()
    {
        // 使用 FileAgentStateStorage 测试真实文件场景
        var tempDir = Path.Combine(Path.GetTempPath(), $"mib_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sessionDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionDir);

            // 写入损坏的 JSON
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "s1.state.json"), "{invalid json");

            var options = new AgentStorageOptions { RootPath = tempDir };
            var storage = new FileAgentStateStorage(
                Microsoft.Extensions.Options.Options.Create(options),
                new FakeLogger<FileAgentStateStorage>());

            var result = await storage.LoadSnapshotAsync("s1");
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private class FakeLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
```

- [ ] **Step 2: 运行测试**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~CheckpointTests"`
Expected: ALL PASSED

- [ ] **Step 3: 提交**

```bash
git add test/ManInBlack.AI.Tests/Middlewares/CheckpointTests.cs
git commit -m "✅ 新增快照保存/加载/恢复测试"
```

---

### Task 9: 全量构建和测试

**Files:**
- 无新文件

- [ ] **Step 1: 全量构建**

Run: `dotnet build ManInBlack.slnx`
Expected: BUILD SUCCEEDED

- [ ] **Step 2: 全量测试**

Run: `dotnet test test/ManInBlack.AI.Tests`
Expected: ALL PASSED

- [ ] **Step 3: 提交（如有遗留修复）**

```bash
git add -A
git commit -m "✅ 全量构建和测试通过"
```

---

### Task 10: 更新文档

**Files:**
- Modify: `docs/architecture.md` — 新增状态持久化章节
- Modify: `docs/middleware-guide.md` — 补充 ReadPersistenceMiddleware 快照恢复说明
- Modify: `docs/configuration-guide.md` — 新增 StatePersistence 配置说明

- [ ] **Step 1: 更新 architecture.md**

在架构文档中新增"状态持久化"章节，说明 `IAgentStateStorage`、`AgentStateSnapshot`、检查点机制和恢复流程。

- [ ] **Step 2: 更新 middleware-guide.md**

补充 `ReadPersistenceMiddleware` 的快照恢复行为和 `SavePersistenceMiddleware` 的 `SessionEnd` 检查点说明。

- [ ] **Step 3: 更新 configuration-guide.md**

新增 `StatePersistence` 配置节说明。

- [ ] **Step 4: 提交**

```bash
git add docs/
git commit -m "📝 更新架构、中间件、配置文档"
```
