# Agent 状态持久化与 Session 恢复

## 背景

当前 ManInBlack 已有消息历史持久化机制（`ISessionStorage` + JSONL 文件），但仅保存 `ChatMessage` 列表。进程崩溃或长任务中断后，`AgentContext.Items`（中间件共享状态）、`SystemPrompt` 等上下文丢失，无法完整恢复 session。

## 目标

- **崩溃恢复**：进程意外终止后，用户可通过已有 `sessionId` 继续对话
- **断点续传**：多步工具调用链中断后，从中断点恢复而非从头开始
- **可插拔存储**：抽象存储接口，默认文件实现，用户可替换
- **透明恢复**：调用方只需传已有 `sessionId`，中间件自动检测并恢复

## 状态范围

保存以下核心状态：

| 字段 | 来源 | 说明 |
|------|------|------|
| Messages | `context.Messages` | 由现有 JSONL 持久化，快照不重复保存 |
| SystemPrompt | `context.SystemPrompt` | 系统提示词 |
| Items | `context.Items` | 中间件共享状态，仅保存可 JSON 序列化的值 |
| AgentName | `context.AgentName` | Agent 定义名称 |
| SessionId | `context.SessionId` | 会话标识 |

## 设计

### 1. 存储抽象

合并 `ISessionStorage` 为 `IAgentStateStorage`：

```csharp
public interface IAgentStateStorage
{
    // 消息历史（原 ISessionStorage 职责）
    Task<IReadOnlyList<ChatMessage>> LoadMessagesAsync(string sessionId, CancellationToken ct);
    Task SaveMessageAsync(string sessionId, ChatMessage message, CancellationToken ct);

    // 状态快照（新增）
    Task<AgentStateSnapshot?> LoadSnapshotAsync(string sessionId, CancellationToken ct);
    Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot, CancellationToken ct);
    Task DeleteSnapshotAsync(string sessionId, CancellationToken ct);
}

public sealed record AgentStateSnapshot
{
    public string SessionId { get; init; } = "";
    public string AgentName { get; init; } = "";
    public string SystemPrompt { get; init; } = "";
    public Dictionary<string, object> Items { get; init; } = [];
    public DateTimeOffset SavedAt { get; init; }
    public string? CheckpointReason { get; init; }  // "ToolCallCompleted" | "SessionEnd"
}
```

`FileAgentStateStorage` 同时实现 `IAgentStateStorage` 和 `ISessionStorage`，确保向后兼容。后续版本移除 `ISessionStorage`。

### 2. 检查点保存

检查点回调通过 `AgentContext.Items["SaveCheckpoint"]` 注入，解耦存储依赖：

```csharp
// AgentFactory.RunAsync 中注入
context.Items["SaveCheckpoint"] = (Func<string?, CancellationToken, Task>)(async (reason, ct) =>
{
    var storage = scope.ServiceProvider.GetRequiredService<IAgentStateStorage>();
    var snapshot = new AgentStateSnapshot
    {
        SessionId = context.SessionId,
        AgentName = context.AgentName,
        SystemPrompt = context.SystemPrompt,
        Items = SerializeItems(context.Items),
        SavedAt = DateTimeOffset.UtcNow,
        CheckpointReason = reason
    };
    await storage.SaveSnapshotAsync(context.SessionId, snapshot, ct);
});
```

Items 序列化逻辑：

```csharp
private static Dictionary<string, object> SerializeItems(IDictionary<string, object> items)
{
    var result = new Dictionary<string, object>();
    foreach (var (key, value) in items)
    {
        if (key == "SaveCheckpoint") continue;
        try
        {
            JsonSerializer.SerializeToElement(value);
            result[key] = value;
        }
        catch
        {
            // 不可序列化的值跳过
        }
    }
    return result;
}
```

### 3. 检查点触发时机

`AgentLoopMiddleware` 中每轮工具调用执行完毕后触发：

```
while (true)
{
    // 1. 调用 LLM，收集响应
    // 2. 追加 assistant 消息
    // 3. 没有工具调用 → 保存最终快照 → yield break

    // 4. 执行所有工具调用，追加 tool 消息
    // 5. 检查点保存（CheckpointReason = "ToolCallCompleted"）
    // 6. 继续循环
}
```

保存失败只记录日志，不影响对话继续。

触发逻辑通过 `ICheckpointPolicy` 可配置：

```csharp
public interface ICheckpointPolicy
{
    bool ShouldSave(AgentContext context, string phase);
}
```

默认实现 `AfterToolCallPolicy` 在每轮工具调用后和 session 结束时都触发。用户可替换策略。

### 4. 恢复机制

`ReadPersistenceMiddleware` 中扩展：

```
OnInvoke:
  1. 尝试 LoadSnapshotAsync(sessionId)
  2. 有快照 → 恢复 Items + SystemPrompt 到 context
  3. 无快照 → 跳过
  4. 注入 SaveCheckpoint 回调到 Items
  5. 继续加载消息历史（原有逻辑）
  6. 调用 next()
```

恢复逻辑：

```csharp
if (snapshot is not null)
{
    context.SystemPrompt = snapshot.SystemPrompt;
    foreach (var (key, value) in snapshot.Items)
        context.Items[key] = value;
}
```

恢复对调用方透明。`CheckpointReason = "ToolCallCompleted"` 表示消息历史已完整，LLM 基于完整上下文自然继续。

### 5. 文件存储默认实现

```
~/.man-in-black/sessions/
  {sessionId}.jsonl          # 消息历史（已有）
  {sessionId}.state.json     # 状态快照（新增）
```

快照文件格式：

```json
{
  "sessionId": "user123_20260514",
  "agentName": "MyAgent",
  "systemPrompt": "...",
  "items": { "SomeMiddleware_Key": "value" },
  "savedAt": "2026-05-14T10:30:00Z",
  "checkpointReason": "ToolCallCompleted"
}
```

- 写入用临时文件 + 原子重命名，避免写一半崩溃导致损坏
- 读取时 JSON 解析失败返回 `null`，记录警告，走新建 session 逻辑

### 6. DI 注册

```csharp
// DependencyInjection.cs
services.TryAddSingleton<IAgentStateStorage, FileAgentStateStorage>();
services.TryAddSingleton<ICheckpointPolicy, AfterToolCallPolicy>();
```

配置项（`ManInBlackOptions`）：

```json
{
  "StatePersistence": {
    "Enabled": true,
    "CleanupOnSessionEnd": false
  }
}
```

- `Enabled`：是否启用检查点保存，默认 `true`
- `CleanupOnSessionEnd`：session 结束后是否删除快照文件，默认 `false`

### 7. 测试

新增 `CheckpointTests.cs`，使用手写 fake（项目约定不用 mock 框架）：

| 用例 | 验证点 |
|------|--------|
| 快照保存与加载 | Items/SystemPrompt 恢复一致 |
| 不可序列化 Items 跳过 | Func/Stream 等被跳过，无异常 |
| 无快照走原有逻辑 | 返回 null，正常加载消息历史 |
| 损坏快照容错 | 无效 JSON 返回 null + 警告日志 |
| 检查点触发时机 | 工具调用后和 session 结束时各触发一次 |

`FakeAgentStateStorage`：内存 `Dictionary` 模拟存储。

现有测试中注入 `ISessionStorage` 的地方改为 `IAgentStateStorage`。
