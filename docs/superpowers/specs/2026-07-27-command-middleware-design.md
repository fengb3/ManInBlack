# Command Middleware 设计

> 状态:Draft · 日期:2026-07-27
> 目标:把 `/`-开头的用户输入做成一套可扩展的命令子系统,与现有「工具子系统」完全对称,并补一个命令生命周期 hook 事件。

## 1. 背景与现状

当前只有 `/new`(别名 `/clear`、`/reset`)一个命令,且其逻辑被硬编码在 `ReadPersistenceMiddleware.HandleAsync`(`src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs:68-92`)里:命中命令时换一个新的 `SessionId`、清空 `Messages`、yield「已重置对话」、`yield break`。命令分发与持久化逻辑耦合在一起,加新命令就得改持久化中间件。

解析器 `UserInputCommandHelper.FetchCommand(input, out name, out args)`(`src/ManInBlack.AI/Services/UserInputCommandHelper.cs`)已经能把 `/cmd a b` 拆成 `name="cmd"`、`args=["a","b"]`,但目前只被 `ReadPersistenceMiddleware` 调用。

代码库已有一套成熟的「特性 + 源生成器 + 注册表 + 派发器」范式(`[AiTool]` → `ToolCallerGenerator` → `ToolRegistry`/`ToolExecutor` → `ToolsMiddleware`/`AgentLoopMiddleware`)。本设计把命令做成它的镜像。

## 2. 目标 / 非目标

**目标**

- 加一个 `[SlashCommand]` 特性,新命令 = 一个方法 + 一个特性,零手工注册。
- 源生成器自动发现命令、生成 handler 与注册表、产出 `/help` 所需元数据、对错误用法报诊断。
- `CommandMiddleware` 在管道前端拦截 `/`-输入,按名派发;命令可短路(不调 `next`)或改完 context 继续 LLM(调 `next`)。
- 把 `/new` 从 `ReadPersistenceMiddleware` 里抽出来,迁成 `[SlashCommand]` 方法。
- 内置 `/help`,从 DI 取注册表列出全部命令。
- 未知 `/xxx` 短路 + 提示「输入 /help」。
- 命令生命周期 hook 事件:`CommandExecutedEvent`(命令名 / 参数 / 是否成功),供 UI 观察者消费;同时接入脚本 hook 系统(`HookPoint.AfterCommand`),让用户能在 `mib-hooks.json` 里写脚本响应命令。

**非目标(本次不做)**

- 强类型位置参数绑定(复用 `ToolCallerEmitter.ConvertExpr`)。
- per-agent 命令分组(对齐工具的 `GetByGroups`)。
- 用户自定义 markdown 命令(Claude Code 的 `.md` 命令)。
- 命令响应的持久化(`SavePersistence` 在内层,命令闲聊默认不持久化,与现状一致)。
- 阻断型 `BeforeCommand` 事件(命令本就是确定性的本地逻辑,暂不需要前置阻断)。

## 3. 总体架构

命令子系统与工具子系统逐层对称:

| 层 | 工具(已有) | 命令(本设计) |
|---|---|---|
| 特性 | `[AiTool]`(标在 `partial` 类的方法上) | `[SlashCommand(name, desc, Aliases)]` |
| 源生成器 | `ToolCallerGenerator` + `ToolCallerEmitter` | `SlashCommandGenerator` + `SlashCommandEmitter` |
| 运行时注册表 | `ToolRegistry` | `SlashCommandRegistry` |
| 运行时派发 | `ToolExecutor`(`AgentLoopMiddleware` 调用) | `CommandMiddleware`(管道前端拦截) |
| DI | `[ServiceRegister.Scoped]` + 源生成器注册 handler | 同 |

数据流:

```
UserInput ─► CommandMiddleware
               ├─ FetchCommand → 不是命令? ─► next() (正常管道)
               ├─ registry.TryGet(name) 命中? ─► handler.ExecuteAsync(context, next, ct)
               │      ├─ (命令内部决定是否调 next() 继续 LLM)
               │      └─ 跑完 → 发 CommandExecutedEvent + IHookExecutor(AfterCommand)
               └─ 未知命令 ─► yield「未知命令 /xxx,输入 /help」
```

## 4. 详细设计

### 4.1 `[SlashCommand]` 特性

文件:`src/ManInBlack.AI.Abstraction/Attributes/SlashCommandAttribute.cs`

```csharp
namespace ManInBlack.AI.Abstraction.Attributes;

/// <summary>标记一个方法为斜杠命令,仅供源生成器识别。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SlashCommandAttribute(string name, string description) : Attribute
{
    /// <summary>命令名(不含前导 /)。</summary>
    public string Name { get; } = name;

    /// <summary>一句话描述,用于 /help。</summary>
    public string Description { get; } = description;

    /// <summary>别名(同样不含 /)。如 /new 的 ["clear","reset"]。</summary>
    public string[] Aliases { get; set; } = [];
}
```

### 4.2 命令方法契约(hybrid,无独立 context 类型)

**不引入 `SlashCommandContext`**——经分析,它只是 `AgentContext` 表面的重复(详见 §8 决策记录)。命令方法签名与 `AgentMiddleware.HandleAsync` 完全一致,最不意外:

```csharp
public async IAsyncEnumerable<ChatResponseUpdate> MethodName(
    AgentContext context,
    ChatResponseUpdateHandler next,
    [EnumeratorCancellation] CancellationToken ct)
```

- **`next`**:保留为参数。这是 `AgentContext` 唯一承载不了的命令专属需求(委托不适合放进会被快照的 `Items`)。不调 `next()` = 短路;调 `next()` 并透传流 = 改完 context 继续 LLM。
- **`CancellationToken`**:`AgentContext.CancellationToken` 已存在,但保留 `ct` 参数以与所有中间件一致,并让 `[EnumeratorCancellation]` 在 `WithCancellation` 时正确传播。
- **参数(Args)**:`CommandMiddleware` 派发前写入 `context.Items[SlashCommandItems.Args]`(键常量)。命令用一个扩展方法读取:

```csharp
// src/ManInBlack.AI/Commands/SlashCommandItems.cs
public static class SlashCommandItems
{
    public const string Args = "__slashCommand.args";
}

public static class SlashCommandContextExtensions
{
    /// <summary>读取 CommandMiddleware 注入的位置参数;无则返回空数组。</summary>
    public static string[] GetCommandArgs(this AgentContext context)
        => context.Items.TryGetValue(SlashCommandItems.Args, out var v) && v is string[] a ? a : [];
}
```

  > **不会泄漏到持久化快照**:命令这一轮在 `SavePersistenceMiddleware`(内层)之前就 `yield break`,快照/持久化中间件根本不执行,`Items` 里的 Args 是一次性的。

内置命令示例(短路型):

```csharp
[ServiceRegister.Scoped]
public partial class BuiltinCommands
{
    /// <summary>重置当前会话:换新 SessionId、清空历史。</summary>
    [SlashCommand("new", "重置对话", Aliases = ["clear", "reset"])]
    public async IAsyncEnumerable<ChatResponseUpdate> New(
        AgentContext context, ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var userStorage = context.ServiceProvider.GetRequiredService<IUserStorage>();
        context.SessionId = await userStorage.CreateNewSessionIdAsync(context.ParentId);
        context.Messages.Clear();

        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("已重置对话")],
        };
        // 不调 next() → 短路,不走 LLM
    }
}
```

继续型(改 context 后透传 LLM 流):

```csharp
[SlashCommand("model", "切换模型")]
public async IAsyncEnumerable<ChatResponseUpdate> Model(
    AgentContext context, ChatResponseUpdateHandler next,
    [EnumeratorCancellation] CancellationToken ct)
{
    var args = context.GetCommandArgs();
    context.Options ??= new ChatOptions();
    context.Options.ModelId = args.Length > 0 ? args[0] : null;
    await foreach (var u in next().WithCancellation(ct))
        yield return u;
}
```

### 4.3 `ICommandHandler` + `SlashCommandRegistry`

文件:`src/ManInBlack.AI.Abstraction/Commands/ICommandHandler.cs`、`src/ManInBlack.AI/Commands/SlashCommandRegistry.cs`

```csharp
public interface ICommandHandler
{
    string CommandName { get; }
    string[] Aliases { get; }
    string Description { get; }

    IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext context, ChatResponseUpdateHandler next, CancellationToken ct);
}

public sealed record CommandInfo(string Name, IReadOnlyList<string> Aliases, string Description);

public sealed class SlashCommandRegistry   // 注册为 Singleton
{
    private readonly Dictionary<string, ICommandHandler> _byKey;  // 名 + 别名,OrdinalIgnoreCase

    public SlashCommandRegistry(IEnumerable<ICommandHandler> handlers)
    {
        _byKey = new(StringComparer.OrdinalIgnoreCase);
        Commands = handlers
            .GroupBy(h => h.CommandName)   // 同一 handler 的别名归并到一条 CommandInfo
            .Select(g => new CommandInfo(
                g.Key,
                g.First().Aliases,
                g.First().Description))
            .ToList();
        foreach (var h in handlers)
        {
            _byKey[h.CommandName] = h;
            foreach (var alias in h.Aliases) _byKey[alias] = h;
        }
    }

    public bool TryGet(string key, [MaybeNullWhen(false)] out ICommandHandler handler)
        => _byKey.TryGetValue(key, out handler);

    /// <summary>去重后的命令清单,供 /help。</summary>
    public IReadOnlyList<CommandInfo> Commands { get; }
}
```

### 4.4 `CommandMiddleware`

文件:`src/ManInBlack.AI/Middlewares/CommandMiddleware.cs`

注入 `SlashCommandRegistry`、`EventBus`、`IHookExecutor`。命中命令时:注入 Args → 跑 handler 流(透传)→ 无论成功/失败都发 `CommandExecutedEvent` 并跑 `HookPoint.AfterCommand` 脚本。

> **关于 `yield` 与 `try/catch`**:C# 不允许在带 `catch` 的 `try` 内 `yield return`。所以成功/失败用一个 `try/finally`(无 catch)判定:`status.Succeeded` 默认 `false`,只有当 handler 流被正常枚举到底才置 `true`;异常会自然从 `await foreach` 向上抛(与现有中间件一致,不在框架层吞),`finally` 仍以 `Succeeded=false` 发事件。事件/脚本的发布逻辑放进一个非迭代器的私有 helper `PublishAndHookAsync`,这样 `finally` 里能 `await` 它。

```csharp
[ServiceRegister.Scoped]
public sealed partial class CommandMiddleware(
    SlashCommandRegistry registry,
    EventBus eventBus,
    IHookExecutor hookExecutor,
    ILogger<CommandMiddleware> logger) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context, ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!UserInputCommandHelper.FetchCommand(context.UserInput, out var name, out var args))
        {
            await foreach (var u in next().WithCancellation(ct)) yield return u;  // 非命令 → 正常管道
            yield break;
        }

        if (!registry.TryGet(name!, out var handler))
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent($"未知命令 /{name}。输入 /help 查看可用命令。")],
            };
            yield break;   // 未知命令:不发事件(没有命令被执行)
        }

        context.Items[SlashCommandItems.Args] = args!;
        var status = new CommandRunStatus();   // { Succeeded = false }
        try
        {
            await foreach (var u in handler!.ExecuteAsync(context, next, ct).WithCancellation(ct))
                yield return u;                // OK:此 try 只有 finally,无 catch
            status.Succeeded = true;           // 仅正常枚举到底才置 true
        }
        finally
        {
            // 异常路径下 status.Succeeded 仍为 false;异常本身继续向上抛
            await PublishAndHookAsync(context, handler!, args!, status.Succeeded, ct);
        }
    }

    /// <summary>非迭代器 helper:发 CommandExecutedEvent(观察者通道)+ 跑 AfterCommand 脚本。</summary>
    private async Task PublishAndHookAsync(
        AgentContext context, ICommandHandler handler, string[] args, bool succeeded, CancellationToken ct)
    {
        var key = context.AgentId;
        var evt = new CommandExecutedEvent
        {
            AgentId = key,
            CommandName = handler.CommandName,
            Args = args,
            Succeeded = succeeded,
        };
        await eventBus.PublishAsync(key, evt, ct);   // 观察者通道 → UI / 测试

        var hookCtx = new HookContext
        {
            HookPoint = HookPoint.AfterCommand.ToString(),
            AgentId = key,
            CommandName = handler.CommandName,
            CommandArgs = JsonSerializer.Serialize(args),
            Succeeded = succeeded,
            Properties = HookProps.From(context),     // 复用 HookMiddleware:34-39 的通用属性集
        };
        try { await hookExecutor.ExecuteAsync(HookPoint.AfterCommand, hookCtx, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "AfterCommand 脚本执行异常: {Cmd}", handler.CommandName); }
    }
}

// 简单可变状态盒,供 try/finally 之间传递成功标志
file sealed class CommandRunStatus { public bool Succeeded; }
```

> **若需在事件里带异常消息(`Error`)**:用 Channel 驱动——在一个非迭代器方法里 `await foreach` 并 `catch` 异常回填 `status.Error`,再经 Channel 把 update 推给迭代器 yield(参考 `PersistingMessageCollection` 的 Channel 模式)。v1 先只保证 `Succeeded` 布尔准确,`Error` 留空。

### 4.5 源生成器

文件:`src/ManInBlack.AI.SourceGenerator/SlashCommandGenerator.cs` + `SlashCommandEmitter.cs`。

完全照搬 `ToolCallerGenerator`/`ToolCallerEmitter` 的结构(同样用 `Fengb3.EasyCodeBuilder`、同样读 `build_property.RootNamespace`)。扫描 `partial` 类里带 `[SlashCommand]` 的方法,为每个生成 handler:

```csharp
// 自动生成(approx)
internal sealed class BuiltinCommands_New_Handler : ICommandHandler
{
    private readonly IServiceProvider _sp;
    public BuiltinCommands_New_Handler(IServiceProvider sp) => _sp = sp;

    public string CommandName => "new";
    public string[] Aliases   => ["clear", "reset"];
    public string Description => "重置对话";

    public IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext context, ChatResponseUpdateHandler next, CancellationToken ct)
    {
        var instance = (_sp.GetService(typeof(BuiltinCommands)) as BuiltinCommands)
            ?? throw new InvalidOperationException("Failed to resolve 'BuiltinCommands'.");
        return instance.New(context, next, ct);
    }
}
```

并生成 `AddSlashCommands(this IServiceCollection)`:把每个 handler 注册为 `ICommandHandler`(Scoped),注册 `SlashCommandRegistry`(Singleton)。

**诊断**(对齐 `MIB01x` 风格):

| ID | 级别 | 含义 |
|---|---|---|
| `MIB020` | Error | 含 `[SlashCommand]` 方法的类必须声明 `partial` |
| `MIB021` | Warning | `[SlashCommand]` 的 `description` 为空 |
| `MIB022` | Error | 命令名/别名在 assembly 内重复 |

> 命名冲突解析:复用 `ToolCallerGenerator.ResolveToolNames` 的「同名加类名前缀」策略。

### 4.6 DI 接线

在 `src/ManInBlack.AI/DependencyInjection.cs:121`(`AddManInBlack()` 内 `services.AddToolHandlers()` 旁)加一行:

```csharp
services.AddToolHandlers();
services.AddSlashCommands();   // 新增
```

### 4.7 管道位置与迁移

`AgentPipelineBuilderExtensions.UseDefault` 顺序变为:

```
EventPublishing → CommandMiddleware → ReadPersistence → SavePersistence → Skill
   → Delegation → Profile → ContextCompress → Tools → (beforeSimple) → UseSimple
```

- 在 `EventPublishingMiddleware` **之内** → 命令响应(如「已重置对话」)对 UI 监听者可见。
- 在 `ReadPersistenceMiddleware` **之前** → `/new` 在加载历史前短路。

`SessionId` 正确性已核实:`AgentFactory.cs:179` 在每轮开始时按 `user.GetLatestSessionId() ?? CreateNewSessionIdAsync(rootUserId)` 解析 `SessionId`,而 `CreateNewSessionIdAsync` 会刷新该用户的「最新会话」并持久化。因此 `/new` 无论在管道哪一层换 SessionId,下一轮都能读到新值。命令轮在 `SavePersistence` 之前短路,本轮不持久化,无副作用。

**迁移步骤:**

1. 新建 `src/ManInBlack.AI/Commands/BuiltinCommands.cs`(partial,`[ServiceRegister.Scoped]`),把 `ReadPersistenceMiddleware` 里的命令逻辑逐字搬成 `[SlashCommand("new", "重置对话", Aliases = ["clear","reset"])]` 方法。
2. 新建 `BuiltinCommands.Help(...)`:`/help` 从 `context.ServiceProvider.GetRequiredService<SlashCommandRegistry>()` 取 `Commands`,格式化成文本 yield。
3. 删除 `PersistenceMiddleware.cs:68-92` 的命令块,`ReadPersistenceMiddleware` 回归纯持久化。
4. DI 加 `AddSlashCommands()`(见 §4.6)。
5. `UseDefault` 在 `EventPublishing` 之后 `.Use<CommandMiddleware>()`。
6. `PersistenceMiddlewareTests` 里 `Clear/Reset/New` 三个测试搬到新的 `CommandMiddlewareTests`(断言:换 SessionId、清空 Messages、yield「已重置」、短路不调 next);非命令的 ReadPersistence 测试保留。

### 4.8 内置命令

| 命令 | 别名 | 行为 | 类型 |
|---|---|---|---|
| `/new` | `/clear`、`/reset` | 换新 SessionId + 清空 Messages + yield「已重置对话」 | 短路 |
| `/help` | — | 从 `SlashCommandRegistry.Commands` 列出「命令名 — 描述」 | 短路 |

### 4.9 命令生命周期 hook

**事件**——`src/ManInBlack.AI/Events/AgentLifecycleEvent.cs` 新增 record(镜像 `AfterToolExecuteEvent`):

```csharp
/// <summary>命令执行后事件(纯通知):命令名、参数、是否成功。</summary>
public record CommandExecutedEvent
{
    public string AgentId { get; init; } = string.Empty;
    public string CommandName { get; init; } = string.Empty;
    public IReadOnlyList<string> Args { get; init; } = [];
    public bool Succeeded { get; init; } = true;
    public string? Error { get; init; }
}
```

`CommandMiddleware` 在 handler 流跑完后,把它发到**观察者通道**(`eventBus.PublishAsync(agentId, evt)`),供 UI / 测试在 `agentId` 上订阅消费(与工具事件供 `FeishuCardSession` 消费的通道相同)。**不**额外发 `HookKey` 通道——命令的脚本 hook 由 `CommandMiddleware` 直接调 `IHookExecutor` 承担(原因见下),`HookKey` 那条没有订阅者。

**脚本 hook**——`HookPoint` 枚举(`src/ManInBlack.AI.Abstraction/Hooks/HookPoint.cs`)新增:

```csharp
/// <summary>斜杠命令执行后(可记录命令名/参数/成功与否)</summary>
AfterCommand,
```

`HookContext` 新增命令专属字段(镜像已有的 `ToolName`/`ArgumentsJson`/`Error`):`CommandName`(string?)、`CommandArgs`(string?,命令参数数组的 JSON)、`Succeeded`(bool);复用已有 `Error` 承载命令错误信息。

> **为何 `CommandMiddleware` 直接调 `IHookExecutor`,而不是像工具那样由 `HookMiddleware` 订阅 EventBus?**
> `CommandMiddleware` 在管道里位于 `HookMiddleware` **之外**(它必须在 `ReadPersistence` 之前才能短路 `/new`,而 `HookMiddleware` 在内层 `UseSimple` 里)。短路命令根本不会进入 `HookMiddleware` 的作用域,其 EventBus 订阅也就没建立。因此命令的脚本 hook 必须由 `CommandMiddleware` 直接调 `IHookExecutor.ExecuteAsync(HookPoint.AfterCommand, ...)`,这样才能覆盖短路命令(如 `/new`),做到「任何命令执行后都能触发脚本」。

脚本侧用法(`.agents/mib-hooks.json`):

```json
[
  {
    "name": "audit-command",
    "hookPoint": "AfterCommand",
    "script": "python audit_command.py",
    "enabled": true
  }
]
```

脚本从 stdin 读 `HookContext` JSON,按 `CommandName` / `Succeeded` 自行过滤(命令不做 `ToolNames` 式的服务端过滤,YAGNI)。

**事件时序**:`CommandExecutedEvent` 与 `AfterCommand` 脚本都在 handler 整条流跑完后触发。对「改完 context 继续 LLM」的命令,这代表事件在 LLM 流结束后才发;`Succeeded` = 整条流没抛异常。

## 5. 边界与错误处理

| 情况 | 行为 |
|---|---|
| 非命令(不以 `/` 开头,或仅 `/`) | `FetchCommand` 返回 false → 正常管道 |
| 已知命令 | 派发到 handler;handler 决定短路或继续 |
| 未知 `/xxx` | 短路 +「未知命令 /xxx。输入 /help」;不发事件 |
| 命令参数不足 | 命令自查 `context.GetCommandArgs().Length` 并 yield 用法提示(框架不强校验,区别于工具的 schema) |
| 取消 | `ct` + `.WithCancellation(ct)` 透传 |
| 命令内抛异常 | 透传(与现有中间件一致);`finally` 仍发 `Succeeded=false` 的事件与 `AfterCommand` 脚本 |
| 脚本异常 | `HookExecutor` 内部已捕获并记录;`CommandMiddleware` 额外包一层 try/catch,绝不阻塞命令 |

## 6. 测试

- **`CommandMiddlewareTests`**
  - 已知命令(`/new`)派发:换 SessionId、清空 Messages、yield「已重置」、不调 next。
  - 别名(`/clear`、`/reset`)与主名等价。
  - 未知命令:`/foobar` yield 提示并短路。
  - 非命令(`hello world`)透传到 next。
  - 命令抛异常:事件 `Succeeded=false`、异常向上抛。
  - 取消:ct 触发后流正确终止。
  - `/help`:产出包含已注册命令名与描述的文本。
- **`SlashCommandRegistryTests`**
  - 名/别名查找、大小写不敏感。
  - `Commands` 去重(别名不重复出现)。
- **`CommandHookTests`**
  - 命令执行后观察者通道收到 `CommandExecutedEvent`(payload 含 CommandName/Args/Succeeded)。
  - 失败时 `Succeeded=false`(v1 `Error` 留空,见 §4.4 注)。
  - `AfterCommand` 脚本被 `IHookExecutor` 调用(用 `FakeHookExecutor` 验证)。
- **源生成器诊断测试**(若有 tool 生成器测试则对齐):非 partial 报 `MIB020`、重名报 `MIB022`、空 description 报 `MIB021`。

## 7. 文件清单

**新增:**
- `src/ManInBlack.AI.Abstraction/Attributes/SlashCommandAttribute.cs`
- `src/ManInBlack.AI.Abstraction/Commands/ICommandHandler.cs`
- `src/ManInBlack.AI/Commands/SlashCommandRegistry.cs`(`CommandInfo`)
- `src/ManInBlack.AI/Commands/SlashCommandItems.cs`(键常量 + `GetCommandArgs` 扩展)
- `src/ManInBlack.AI/Commands/BuiltinCommands.cs`(`/new`、`/help`)
- `src/ManInBlack.AI/Middlewares/CommandMiddleware.cs`
- `src/ManInBlack.AI.SourceGenerator/SlashCommandGenerator.cs` + `SlashCommandEmitter.cs`
- 测试:`CommandMiddlewareTests`、`SlashCommandRegistryTests`、`CommandHookTests`

**修改:**
- `src/ManInBlack.AI/Events/AgentLifecycleEvent.cs`(加 `CommandExecutedEvent`)
- `src/ManInBlack.AI.Abstraction/Hooks/HookPoint.cs`(加 `AfterCommand`)
- `src/ManInBlack.AI.Abstraction/Hooks/HookContext.cs`(加 `CommandName`/`CommandArgs`/`Succeeded`)
- `src/ManInBlack.AI/AgentPipelines.cs`(`UseDefault` 插入 `CommandMiddleware`)
- `src/ManInBlack.AI/DependencyInjection.cs`(`AddSlashCommands()`)
- `src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs`(删命令块)
- `test/ManInBlack.AI.Tests/Middlewares/PersistenceMiddlewareTests.cs`(迁移 3 个命令测试)

## 8. 决策记录

- **注册模型:特性 + 源生成器**(而非接口+DI 或委托字典)。与现有 `ToolCallerGenerator` 范式一致,新命令零手工注册,且天然产出 `/help` 元数据与诊断。
- **不引入 `SlashCommandContext`**。逐一排查「只有它能覆盖、`AgentContext` 覆盖不了」的场景,无一是 v1 必需:`Next` 用方法参数承载,`Args` 走 `Items`,`CancellationToken`/`SessionId`/`Messages`/`Options`/`SystemPrompt`/`Items` `AgentContext` 本来就有,命令内 scratch 状态用局部变量,未来强类型参数绑定不依赖现在建一个 context 类型。引入它只会重复 `AgentContext` 表面。
- **未知命令短路 + 提示**(不透传给 LLM)。`/`-前缀输入几乎总是命令意图,透传易把无意义文本喂给模型。
- **`/help` 内置**:用它兑现「源生成器送元数据」的价值,成本极低。
- **脚本 hook 直接调 `IHookExecutor`**:因 `CommandMiddleware` 在 `HookMiddleware` 之外、短路命令不进入后者作用域(详见 §4.9)。

## 9. 未来演进(非本次)

- 强类型位置参数绑定(`[SlashCommand] public ... Run(string target, int count=1)`),复用 `ToolCallerEmitter` 的 `ConvertExpr`,让源生成器按位置把 `string[]` args 绑到形参。届时 `GetCommandArgs` 可弃用。
- per-agent 命令分组(对齐 `ToolRegistry.GetByGroups`)。
- 用户自定义 markdown 命令(读 `{workspace}/.agents/commands/*.md`,把正文作为 prompt 注入)。
- `BeforeCommand` 阻断型事件(若需要按命令名拦截)。
