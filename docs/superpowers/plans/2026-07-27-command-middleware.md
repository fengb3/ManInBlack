# Command Middleware Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an extensible `/`-command subsystem (attribute + source generator + registry + middleware) mirroring the existing tool subsystem, migrate `/new` out of `ReadPersistenceMiddleware`, add `/help`, and emit a `CommandExecutedEvent` lifecycle hook with an `AfterCommand` script hook.

**Architecture:** `[SlashCommand]` methods are discovered by a new `SlashCommandGenerator` (sibling to `ToolCallerGenerator`), which emits per-command `ICommandHandler` classes + an `AddSlashCommands()` DI extension. `CommandMiddleware` sits right after `EventPublishingMiddleware`, parses `/`-input via the existing `UserInputCommandHelper`, dispatches to the registry, and on completion publishes `CommandExecutedEvent` + calls `IHookExecutor(AfterCommand)`.

**Tech Stack:** .NET 10, C# `IIncrementalGenerator` (Roslyn 4.11), `Fengb3.EasyCodeBuilder`, xUnit, `Microsoft.Extensions.AI` / `Microsoft.Extensions.DependencyInjection`.

**Spec:** `docs/superpowers/specs/2026-07-27-command-middleware-design.md`

**Deviation from spec (noted):** `SlashCommandRegistry` is registered **Scoped**, not Singleton. It depends on `IEnumerable<ICommandHandler>` (Scoped handlers); a Singleton registry would be a captive dependency whose handlers resolve `BuiltinCommands` from the root scope. Scoped is correct and cheap.

---

## File Structure

**New files**
- `src/ManInBlack.AI.Abstraction/Attributes/SlashCommandAttribute.cs` — the attribute.
- `src/ManInBlack.AI.Abstraction/Commands/ICommandHandler.cs` — handler interface + `CommandInfo` record.
- `src/ManInBlack.AI/Commands/SlashCommandRegistry.cs` — name/alias → handler map + `/help` list.
- `src/ManInBlack.AI/Commands/SlashCommandItems.cs` — `Items` key constant + `GetCommandArgs()` extension.
- `src/ManInBlack.AI/Commands/BuiltinCommands.cs` — `/new` and `/help`.
- `src/ManInBlack.AI/Middlewares/CommandMiddleware.cs` — dispatcher + hook emitter.
- `src/ManInBlack.AI.SourceGenerator/CommandMethodModel.cs` — generator model.
- `src/ManInBlack.AI.SourceGenerator/SlashCommandGenerator.cs` — scanner + diagnostics.
- `src/ManInBlack.AI.SourceGenerator/SlashCommandEmitter.cs` — code emitter.
- `test/ManInBlack.AI.Tests/Commands/SlashCommandRegistryTests.cs`
- `test/ManInBlack.AI.Tests/Middlewares/CommandMiddlewareTests.cs`
- `test/ManInBlack.AI.Tests/Commands/BuiltinCommandsTests.cs`
- `test/ManInBlack.AI.Tests/Commands/SlashCommandGeneratorTests.cs` — integration test via `AddSlashCommands()`.

**Modified files**
- `src/ManInBlack.AI/Events/AgentLifecycleEvent.cs` — add `CommandExecutedEvent`.
- `src/ManInBlack.AI.Abstraction/Hooks/HookPoint.cs` — add `AfterCommand`.
- `src/ManInBlack.AI.Abstraction/Hooks/HookContext.cs` — add `CommandName`/`CommandArgs`/`Succeeded`.
- `src/ManInBlack.AI/AgentPipelines.cs` — insert `CommandMiddleware`.
- `src/ManInBlack.AI/DependencyInjection.cs` — call `AddSlashCommands()`.
- `src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs` — delete the command block.
- `test/ManInBlack.AI.Tests/Middlewares/PersistenceMiddlewareTests.cs` — delete the 3 command tests.

---

### Task 1: `[SlashCommand]` attribute, `ICommandHandler`, `CommandInfo`

**Files:**
- Create: `src/ManInBlack.AI.Abstraction/Attributes/SlashCommandAttribute.cs`
- Create: `src/ManInBlack.AI.Abstraction/Commands/ICommandHandler.cs`
- Test: `test/ManInBlack.AI.Tests/Commands/SlashCommandRegistryTests.cs` (created here, filled in Task 2; here just a placeholder compile check)

- [ ] **Step 1: Write the attribute**

`src/ManInBlack.AI.Abstraction/Attributes/SlashCommandAttribute.cs`:

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

    /// <summary>别名(同样不含 /)。</summary>
    public string[] Aliases { get; set; } = [];
}
```

- [ ] **Step 2: Write the interface + CommandInfo**

`src/ManInBlack.AI.Abstraction/Commands/ICommandHandler.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Commands;

/// <summary>单个斜杠命令的执行器(由源生成器为每个 [SlashCommand] 方法生成实现)。</summary>
public interface ICommandHandler
{
    string CommandName { get; }
    string[] Aliases { get; }
    string Description { get; }

    IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext context, ChatResponseUpdateHandler next, CancellationToken ct);
}

/// <summary>去重后的命令元数据,供 /help 展示。</summary>
public sealed record CommandInfo(string Name, IReadOnlyList<string> Aliases, string Description);
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build src/ManInBlack.AI.Abstraction/ManInBlack.AI.Abstraction.csproj`
Expected: Build succeeded, no errors.

- [ ] **Step 4: Commit**

```bash
git add src/ManInBlack.AI.Abstraction/Attributes/SlashCommandAttribute.cs src/ManInBlack.AI.Abstraction/Commands/ICommandHandler.cs
git commit -m "Add SlashCommandAttribute and ICommandHandler"
```

---

### Task 2: `SlashCommandRegistry`

**Files:**
- Create: `src/ManInBlack.AI/Commands/SlashCommandRegistry.cs`
- Test: `test/ManInBlack.AI.Tests/Commands/SlashCommandRegistryTests.cs`

- [ ] **Step 1: Write the failing tests**

`test/ManInBlack.AI.Tests/Commands/SlashCommandRegistryTests.cs`:

```csharp
using ManInBlack.AI.Abstraction.Commands;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Commands;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Commands;

// 用于测试的假 handler:不需要源生成器即可验证注册表逻辑
file sealed class FakeHandler : ICommandHandler
{
    public string CommandName { get; init; } = "";
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = "";
    public IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext context, ChatResponseUpdateHandler next, CancellationToken ct)
        => AsyncEnumerable.Empty<ChatResponseUpdate>();
}

public class SlashCommandRegistryTests
{
    [Fact]
    public void TryGet_FindsByCommandName()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new", Description = "重置对话" }
        });

        Assert.True(registry.TryGet("new", out var h));
        Assert.Equal("new", h!.CommandName);
    }

    [Fact]
    public void TryGet_FindsByAlias()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new", Aliases = ["clear", "reset"] }
        });

        Assert.True(registry.TryGet("clear", out _));
        Assert.True(registry.TryGet("reset", out _));
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new" }
        });

        Assert.True(registry.TryGet("NEW", out _));
        Assert.True(registry.TryGet("New", out _));
    }

    [Fact]
    public void TryGet_ReturnsFalseForUnknown()
        => Assert.False(new SlashCommandRegistry([]).TryGet("nope", out _));

    [Fact]
    public void Commands_DedupsAliases()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new", Aliases = ["clear", "reset"], Description = "重置对话" },
            new FakeHandler { CommandName = "help", Description = "帮助" }
        });

        Assert.Equal(2, registry.Commands.Count);
        var newInfo = Assert.Single(registry.Commands.Where(c => c.Name == "new"));
        Assert.Equal(new[] { "clear", "reset" }, newInfo.Aliases);
        Assert.Equal("重置对话", newInfo.Description);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~SlashCommandRegistryTests"`
Expected: FAIL — `SlashCommandRegistry` does not exist (compile error).

- [ ] **Step 3: Implement the registry**

`src/ManInBlack.AI/Commands/SlashCommandRegistry.cs`:

```csharp
using ManInBlack.AI.Abstraction.Commands;

namespace ManInBlack.AI.Commands;

/// <summary>
/// 命令注册表:按命令名/别名(大小写不敏感)查找 <see cref="ICommandHandler"/>,
/// 并提供去重后的 <see cref="CommandInfo"/> 清单供 /help 展示。
/// </summary>
public sealed class SlashCommandRegistry
{
    private readonly Dictionary<string, ICommandHandler> _byKey;

    public SlashCommandRegistry(IEnumerable<ICommandHandler> handlers)
    {
        _byKey = new Dictionary<string, ICommandHandler>(StringComparer.OrdinalIgnoreCase);

        // 去重:同一 handler 实例只产出一条 CommandInfo
        var ordered = handlers.ToList();
        Commands = ordered
            .Select(h => new CommandInfo(h.CommandName, (IReadOnlyList<string>)h.Aliases, h.Description))
            .ToList();

        foreach (var h in ordered)
        {
            _byKey[h.CommandName] = h;
            foreach (var alias in h.Aliases)
                _byKey[alias] = h;
        }
    }

    public bool TryGet(string key, out ICommandHandler? handler)
        => _byKey.TryGetValue(key, out handler);

    /// <summary>去重后的命令清单(不含别名条目),供 /help。</summary>
    public IReadOnlyList<CommandInfo> Commands { get; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~SlashCommandRegistryTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/Commands/SlashCommandRegistry.cs test/ManInBlack.AI.Tests/Commands/SlashCommandRegistryTests.cs
git commit -m "Add SlashCommandRegistry with name/alias lookup"
```

---

### Task 3: `SlashCommandItems` constants + `GetCommandArgs` extension

**Files:**
- Create: `src/ManInBlack.AI/Commands/SlashCommandItems.cs`
- Test: `test/ManInBlack.AI.Tests/Commands/SlashCommandItemsTests.cs`

- [ ] **Step 1: Write the failing test**

`test/ManInBlack.AI.Tests/Commands/SlashCommandItemsTests.cs`:

```csharp
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Commands;
using ManInBlack.AI.Tests.Helpers;
using Xunit;

namespace ManInBlack.AI.Tests.Commands;

public class SlashCommandItemsTests
{
    [Fact]
    public void GetCommandArgs_ReturnsEmpty_WhenNotSet()
    {
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider);
        Assert.Empty(ctx.GetCommandArgs());
    }

    [Fact]
    public void GetCommandArgs_ReturnsInjectedArgs()
    {
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider);
        ctx.Items[SlashCommandItems.Args] = new[] { "a", "b" };

        Assert.Equal(new[] { "a", "b" }, ctx.GetCommandArgs());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~SlashCommandItemsTests"`
Expected: FAIL — `SlashCommandItems` / `GetCommandArgs` do not exist.

- [ ] **Step 3: Implement**

`src/ManInBlack.AI/Commands/SlashCommandItems.cs`:

```csharp
using ManInBlack.AI.Abstraction.Middleware;

namespace ManInBlack.AI.Commands;

/// <summary>
/// 命令子系统在 <see cref="AgentContext.Items"/> 里使用的键,以及读取命令参数的扩展。
/// </summary>
public static class SlashCommandItems
{
    /// <summary>CommandMiddleware 派发前把解析好的命令参数(string[])写入此键。</summary>
    public const string Args = "__slashCommand.args";
}

public static class SlashCommandContextExtensions
{
    /// <summary>读取 CommandMiddleware 注入的位置参数;未注入时返回空数组。</summary>
    public static string[] GetCommandArgs(this AgentContext context)
        => context.Items.TryGetValue(SlashCommandItems.Args, out var v) && v is string[] a ? a : [];
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~SlashCommandItemsTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/Commands/SlashCommandItems.cs test/ManInBlack.AI.Tests/Commands/SlashCommandItemsTests.cs
git commit -m "Add SlashCommandItems key and GetCommandArgs extension"
```

---

### Task 4: `HookPoint.AfterCommand`, `HookContext` fields, `CommandExecutedEvent`

**Files:**
- Modify: `src/ManInBlack.AI.Abstraction/Hooks/HookPoint.cs`
- Modify: `src/ManInBlack.AI.Abstraction/Hooks/HookContext.cs`
- Modify: `src/ManInBlack.AI/Events/AgentLifecycleEvent.cs`
- Test: `test/ManInBlack.AI.Tests/Hooks/CommandHookModelTests.cs`

- [ ] **Step 1: Write the failing tests**

`test/ManInBlack.AI.Tests/Hooks/CommandHookModelTests.cs`:

```csharp
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Events;
using Xunit;

namespace ManInBlack.AI.Tests.Hooks;

public class CommandHookModelTests
{
    [Fact]
    public void HookPoint_HasAfterCommand()
        => Assert.Equal("AfterCommand", nameof(HookPoint.AfterCommand));

    [Fact]
    public void HookContext_CarriesCommandFields()
    {
        var ctx = new HookContext
        {
            CommandName = "new",
            CommandArgs = "[\"arg\"]",
            Succeeded = true,
        };
        Assert.Equal("new", ctx.CommandName);
        Assert.Equal("[\"arg\"]", ctx.CommandArgs);
        Assert.True(ctx.Succeeded);
    }

    [Fact]
    public void CommandExecutedEvent_DefaultsSucceededTrue()
    {
        var evt = new CommandExecutedEvent { CommandName = "new" };
        Assert.True(evt.Succeeded);
        Assert.Equal("new", evt.CommandName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~CommandHookModelTests"`
Expected: FAIL — `AfterCommand`, `CommandName`, `CommandArgs`, `Succeeded`, `CommandExecutedEvent` do not exist.

- [ ] **Step 3: Add `AfterCommand` to `HookPoint`**

In `src/ManInBlack.AI.Abstraction/Hooks/HookPoint.cs`, add this member before the closing brace of the enum (after `AgentCompleted`):

```csharp
    /// <summary>斜杠命令执行后(可记录命令名/参数/成功与否)</summary>
    AfterCommand,
```

- [ ] **Step 4: Add command fields to `HookContext`**

In `src/ManInBlack.AI.Abstraction/Hooks/HookContext.cs`, add these properties (next to `Error`):

```csharp
    /// <summary>命令名(AfterCommand 时可用)</summary>
    public string? CommandName { get; init; }

    /// <summary>命令参数数组的 JSON(AfterCommand 时可用)</summary>
    public string? CommandArgs { get; init; }

    /// <summary>命令是否执行成功(AfterCommand 时可用)</summary>
    public bool Succeeded { get; init; }
```

- [ ] **Step 5: Add `CommandExecutedEvent`**

In `src/ManInBlack.AI/Events/AgentLifecycleEvent.cs`, append this record at the end of the file (after `SubAgentCompletedEvent`):

```csharp
/// <summary>
/// 命令执行后事件(纯通知):命令名、参数、是否成功。
/// </summary>
public record CommandExecutedEvent
{
    public string AgentId { get; init; } = string.Empty;
    public string CommandName { get; init; } = string.Empty;
    public IReadOnlyList<string> Args { get; init; } = [];
    public bool Succeeded { get; init; } = true;
    public string? Error { get; init; }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~CommandHookModelTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ManInBlack.AI.Abstraction/Hooks/HookPoint.cs src/ManInBlack.AI.Abstraction/Hooks/HookContext.cs src/ManInBlack.AI/Events/AgentLifecycleEvent.cs test/ManInBlack.AI.Tests/Hooks/CommandHookModelTests.cs
git commit -m "Add AfterCommand hook point, HookContext command fields, CommandExecutedEvent"
```

---

### Task 5: `CommandMiddleware`

**Files:**
- Create: `src/ManInBlack.AI/Middlewares/CommandMiddleware.cs`
- Test: `test/ManInBlack.AI.Tests/Middlewares/CommandMiddlewareTests.cs`

- [ ] **Step 1: Write the failing tests**

`test/ManInBlack.AI.Tests/Middlewares/CommandMiddlewareTests.cs`:

```csharp
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Commands;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Commands;
using ManInBlack.AI.Events;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Services;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

file sealed class FakeCommandHandler : ICommandHandler
{
    public string CommandName { get; init; } = "new";
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = "";
    public Func<AgentContext, ChatResponseUpdateHandler, CancellationToken,
        IAsyncEnumerable<ChatResponseUpdate>>? Impl { get; set; }
    public IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext c, ChatResponseUpdateHandler n, CancellationToken ct)
        => Impl?.Invoke(c, n, ct) ?? AsyncEnumerable.Empty<ChatResponseUpdate>();
}

public class CommandMiddlewareTests
{
    private static AgentContext NewContext(string userInput, out EventBus bus, out FakeHookExecutor hooks)
    {
        bus = new EventBus();
        hooks = new FakeHookExecutor();
        var services = new ServiceCollection()
            .AddSingleton(bus)
            .BuildServiceProvider();
        return new AgentContext(services)
        {
            AgentId = "agent-1",
            UserInput = userInput,
            Messages = [new(ChatRole.User, userInput)],
        };
    }

    private static CommandMiddleware NewMiddleware(SlashCommandRegistry registry, EventBus bus, FakeHookExecutor hooks)
        => new(registry, bus, hooks, NullLogger<CommandMiddleware>.Instance);

    [Fact]
    public async Task KnownCommand_IsDispatched_AndShortCircuits()
    {
        var ctx = NewContext("/new", out var bus, out var hooks);
        ChatResponseUpdate[] nextStream = [new(ChatRole.Assistant, [new TextContent("SHOULD-NOT-APPEAR")])];
        var handler = new FakeCommandHandler
        {
            CommandName = "new",
            Impl = (_, _, _) => TestHelpers.AsyncSeq(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("已重置")])),
        };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        var results = await middleware.HandleAsync(ctx, () => nextStream.ToAsyncEnumerable()).ToListAsync();

        Assert.Equal("已重置", results.Single().Text);
        Assert.DoesNotContain(results, u => u.Text == "SHOULD-NOT-APPEAR"); // next 未被调用
    }

    [Fact]
    public async Task KnownCommand_InjectsArgsIntoItems()
    {
        var ctx = NewContext("/model sonnet-4", out var bus, out var hooks);
        string[]? captured = null;
        var handler = new FakeCommandHandler
        {
            CommandName = "model",
            Impl = (c, _, _) =>
            {
                captured = c.GetCommandArgs();
                return AsyncEnumerable.Empty<ChatResponseUpdate>();
            },
        };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Equal(new[] { "sonnet-4" }, captured);
    }

    [Fact]
    public async Task UnknownCommand_YieldsHint_AndShortCircuits()
    {
        var ctx = NewContext("/foobar", out var bus, out var hooks);
        var middleware = NewMiddleware(new SlashCommandRegistry([]), bus, hooks);

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("LLM")]))).ToListAsync();

        Assert.Contains("未知命令 /foobar", results.Single().Text);
    }

    [Fact]
    public async Task NonCommand_PassesThroughToNext()
    {
        var ctx = NewContext("hello world", out var bus, out var hooks);
        var middleware = NewMiddleware(new SlashCommandRegistry([]), bus, hooks);

        var results = await middleware.HandleAsync(ctx, () => TestHelpers.AsyncSeq(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("LLM-REPLY")]))).ToListAsync();

        Assert.Equal("LLM-REPLY", results.Single().Text);
    }

    [Fact]
    public async Task AfterRun_PublishesCommandExecutedEvent()
    {
        var ctx = NewContext("/new", out var bus, out var hooks);
        CommandExecutedEvent? captured = null;
        bus.Subscribe<CommandExecutedEvent>("agent-1", (evt, _) => { captured = evt; return Task.CompletedTask; });
        var handler = new FakeCommandHandler { CommandName = "new" };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.NotNull(captured);
        Assert.Equal("new", captured!.CommandName);
        Assert.True(captured.Succeeded);
    }

    [Fact]
    public async Task AfterRun_CallsAfterCommandHook()
    {
        var ctx = NewContext("/new", out var bus, out var hooks);
        var handler = new FakeCommandHandler { CommandName = "new" };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var call = Assert.Single(hooks.ExecutedHooks);
        Assert.Equal(HookPoint.AfterCommand, call.Point);
        Assert.Equal("new", call.Context.CommandName);
        Assert.True(call.Context.Succeeded);
    }

    [Fact]
    public async Task HandlerThrows_PublishesFailedEvent_AndRethrows()
    {
        var ctx = NewContext("/new", out var bus, out var hooks);
        CommandExecutedEvent? captured = null;
        bus.Subscribe<CommandExecutedEvent>("agent-1", (evt, _) => { captured = evt; return Task.CompletedTask; });
        var handler = new FakeCommandHandler
        {
            CommandName = "new",
            Impl = (_, _, _) => TestHelpers.ThrowOnMoveNext<ChatResponseUpdate>(new InvalidOperationException("boom")),
        };
        var middleware = NewMiddleware(new SlashCommandRegistry([handler]), bus, hooks);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync());

        Assert.NotNull(captured);
        Assert.False(captured!.Succeeded);
        Assert.Single(hooks.ExecutedHooks); // finally 仍触发 AfterCommand
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~CommandMiddlewareTests"`
Expected: FAIL — `CommandMiddleware` does not exist.

- [ ] **Step 3: Implement `CommandMiddleware`**

`src/ManInBlack.AI/Middlewares/CommandMiddleware.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Commands;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Commands;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 命令中间件:拦截 <c>/</c>-开头的用户输入,按命令名派发给 <see cref="SlashCommandRegistry"/>。
/// 命令可短路(不调 next)或改完 context 继续 LLM(调 next)。命令执行后发布
/// <see cref="CommandExecutedEvent"/> 并触发 <see cref="HookPoint.AfterCommand"/> 脚本。
/// </summary>
[ServiceRegister.Scoped]
public sealed partial class CommandMiddleware(
    SlashCommandRegistry registry,
    EventBus eventBus,
    IHookExecutor hookExecutor,
    ILogger<CommandMiddleware> logger) : AgentMiddleware
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context, ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!UserInputCommandHelper.FetchCommand(context.UserInput, out var name, out var args))
        {
            await foreach (var u in next().WithCancellation(ct)) yield return u;
            yield break;
        }

        if (!registry.TryGet(name!, out var handler))
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent($"未知命令 /{name}。输入 /help 查看可用命令。")],
            };
            yield break;
        }

        context.Items[SlashCommandItems.Args] = args!;
        var status = new CommandRunStatus();   // Succeeded = false 直到正常枚举到底
        try
        {
            await foreach (var u in handler!.ExecuteAsync(context, next, ct).WithCancellation(ct))
                yield return u;
            status.Succeeded = true;
        }
        finally
        {
            // 异常路径下 status.Succeeded 仍为 false;异常本身继续向上抛
            await PublishAndHookAsync(context, handler!, args!, status.Succeeded, ct);
        }
    }

    private async Task PublishAndHookAsync(
        AgentContext context, ICommandHandler handler, string[] args, bool succeeded, CancellationToken ct)
    {
        var key = context.AgentId;

        await eventBus.PublishAsync(key, new CommandExecutedEvent
        {
            AgentId = key,
            CommandName = handler.CommandName,
            Args = args,
            Succeeded = succeeded,
        }, ct);

        var hookCtx = new HookContext
        {
            HookPoint = HookPoint.AfterCommand.ToString(),
            AgentId = key,
            CommandName = handler.CommandName,
            CommandArgs = JsonSerializer.Serialize(args, JsonOpts),
            Succeeded = succeeded,
            Properties = BuildProps(context),
        };
        try
        {
            await hookExecutor.ExecuteAsync(HookPoint.AfterCommand, hookCtx, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AfterCommand 脚本执行异常: {Cmd}", handler.CommandName);
        }
    }

    private static Dictionary<string, string> BuildProps(AgentContext context)
    {
        var props = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(context.RootUserId)) props["RootUserId"] = context.RootUserId;
        if (!string.IsNullOrEmpty(context.SessionId))  props["SessionId"]  = context.SessionId;
        if (!string.IsNullOrEmpty(context.ParentId))   props["ParentId"]   = context.ParentId;
        if (!string.IsNullOrEmpty(context.ParentType)) props["ParentType"] = context.ParentType;
        if (!string.IsNullOrEmpty(context.AgentName))  props["AgentName"]  = context.AgentName;
        return props;
    }
}

file sealed class CommandRunStatus { public bool Succeeded; }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~CommandMiddlewareTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/Middlewares/CommandMiddleware.cs test/ManInBlack.AI.Tests/Middlewares/CommandMiddlewareTests.cs
git commit -m "Add CommandMiddleware with dispatch, event, and AfterCommand hook"
```

---

### Task 6: `BuiltinCommands` (`/new`, `/help`)

**Files:**
- Create: `src/ManInBlack.AI/Commands/BuiltinCommands.cs`
- Test: `test/ManInBlack.AI.Tests/Commands/BuiltinCommandsTests.cs`

- [ ] **Step 1: Write the failing tests**

`test/ManInBlack.AI.Tests/Commands/BuiltinCommandsTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Commands;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Commands;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Commands;

public class BuiltinCommandsTests
{
    [Fact]
    public async Task New_ResetsSession_ClearsMessages_YieldsConfirmation()
    {
        var userStorage = new FakeUserStorage();
        var services = new ServiceCollection()
            .AddSingleton(userStorage)
            .BuildServiceProvider();
        var ctx = new AgentContext(services)
        {
            SessionId = "old-session",
            ParentId = "u1",
            Messages = [new(ChatRole.User, "/new"), new(ChatRole.Assistant, "old reply")],
        };
        var cmd = new BuiltinCommands();

        var results = await cmd.New(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.NotEqual("old-session", ctx.SessionId);          // 换了新 SessionId
        Assert.Empty(ctx.Messages);                              // 清空了
        Assert.Contains("已重置", results.Single().Text);
    }

    [Fact]
    public async Task Help_ListsRegisteredCommands()
    {
        var registry = new SlashCommandRegistry(new ICommandHandler[]
        {
            new FakeHandler { CommandName = "new", Description = "重置对话" },
            new FakeHandler { CommandName = "help", Description = "帮助" },
        });
        var ctx = new AgentContext(new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider())
        {
            AgentId = "a1",
            UserInput = "/help",
            Messages = [],
        };
        var cmd = new BuiltinCommands();

        var results = await cmd.Help(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        Assert.Contains("new", results.Single().Text);
        Assert.Contains("help", results.Single().Text);
    }
}

file sealed class FakeHandler : ICommandHandler
{
    public string CommandName { get; init; } = "";
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = "";
    public IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext c, ChatResponseUpdateHandler n, CancellationToken ct)
        => AsyncEnumerable.Empty<ChatResponseUpdate>();
}
```

- [ ] **Step 2: Run the `/new` test to verify it fails**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~BuiltinCommandsTests.New_ResetsSession"`
Expected: FAIL — `BuiltinCommands` does not exist.

- [ ] **Step 3: Implement `BuiltinCommands`**

`src/ManInBlack.AI/Commands/BuiltinCommands.cs`:

```csharp
using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Commands;

/// <summary>内置斜杠命令。</summary>
[ServiceRegister.Scoped]
public sealed partial class BuiltinCommands
{
    /// <summary>重置当前会话:换新 SessionId、清空历史,并返回确认。</summary>
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
        // 不调 next() → 短路
    }

    /// <summary>列出全部已注册命令及描述。</summary>
    [SlashCommand("help", "显示可用命令")]
    public async IAsyncEnumerable<ChatResponseUpdate> Help(
        AgentContext context, ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var registry = context.ServiceProvider.GetRequiredService<SlashCommandRegistry>();

        var lines = registry.Commands
            .Select(c => c.Aliases.Count > 0
                ? $"  /{c.Name} (或 /{string.Join(", /", c.Aliases)}) — {c.Description}"
                : $"  /{c.Name} — {c.Description}")
            .ToList();

        var text = lines.Count == 0
            ? "暂无可用命令。"
            : "可用命令:\n" + string.Join("\n", lines);

        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)],
        };
    }
}
```

- [ ] **Step 4: Run the `/new` test to verify it passes**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~BuiltinCommandsTests.New_ResetsSession"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ManInBlack.AI/Commands/BuiltinCommands.cs test/ManInBlack.AI.Tests/Commands/BuiltinCommandsTests.cs
git commit -m "Add BuiltinCommands: /new and /help"
```

---

### Task 7: Source generator (`SlashCommandGenerator` + `SlashCommandEmitter`)

**Files:**
- Create: `src/ManInBlack.AI.SourceGenerator/CommandMethodModel.cs`
- Create: `src/ManInBlack.AI.SourceGenerator/SlashCommandGenerator.cs`
- Create: `src/ManInBlack.AI.SourceGenerator/SlashCommandEmitter.cs`
- Test: `test/ManInBlack.AI.Tests/Commands/SlashCommandGeneratorTests.cs`

- [ ] **Step 1: Write the integration test**

This proves the generator discovered `BuiltinCommands.New` + `BuiltinCommands.Help`, emitted handlers, and `AddSlashCommands()` registered them. It calls `ManInBlack.AI`'s internal `AddSlashCommands()` (visible to tests via `InternalsVisibleTo`).

`test/ManInBlack.AI.Tests/Commands/SlashCommandGeneratorTests.cs`:

```csharp
using ManInBlack.AI;            // AddSlashCommands() extension (generated, internal)
using ManInBlack.AI.Commands;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Commands;

public class SlashCommandGeneratorTests
{
    [Fact]
    public void AddSlashCommands_RegistersNewAndHelp_WithAliases()
    {
        var services = new ServiceCollection();
        services.AddSlashCommands();
        var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<SlashCommandRegistry>();

        Assert.True(registry.TryGet("new", out _));
        Assert.True(registry.TryGet("clear", out _));
        Assert.True(registry.TryGet("reset", out _));
        Assert.True(registry.TryGet("help", out _));

        var names = registry.Commands.Select(c => c.Name).ToList();
        Assert.Contains("new", names);
        Assert.Contains("help", names);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~SlashCommandGeneratorTests"`
Expected: FAIL — `AddSlashCommands()` does not exist (no generator yet, so nothing was generated).

- [ ] **Step 3: Add the model**

`src/ManInBlack.AI.SourceGenerator/CommandMethodModel.cs`:

```csharp
using System.Collections.Generic;

namespace ManInBlack.AI.SourceGenerator;

/// <summary>扫描到的 [SlashCommand] 方法的模型数据。</summary>
public sealed class CommandMethodModel
{
    public string MethodName { get; set; } = "";
    public string ContainingTypeName { get; set; } = "";        // 全称,用于代码生成
    public string ContainingTypeShortName { get; set; } = "";   // 短名,用于错误信息
    public string CommandName { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public bool IsPartialClass { get; set; }
}
```

- [ ] **Step 4: Add the emitter**

`src/ManInBlack.AI.SourceGenerator/SlashCommandEmitter.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Fengb3.EasyCodeBuilder;
using Fengb3.EasyCodeBuilder.Csharp;
using Fengb3.EasyCodeBuilder.Csharp.OptionConfigurations;

namespace ManInBlack.AI.SourceGenerator;

/// <summary>
/// 为每个 [SlashCommand] 方法生成 ICommandHandler 实现类 + AddSlashCommands() DI 注册扩展。
/// </summary>
public static class SlashCommandEmitter
{
    public static string Emit(string namespaceName, List<CommandMethodModel> commands)
    {
        var option = Code.Create()
            .AppendLines("// <auto-generated/>", "#nullable enable", "#pragma warning disable CS1998, CS8602, CS8604")
            .Using(
                "System",
                "System.Collections.Generic",
                "System.Threading",
                "System.Threading.Tasks",
                "ManInBlack.AI.Abstraction.Commands",
                "ManInBlack.AI.Abstraction.Middleware",
                "ManInBlack.AI.Commands",
                "Microsoft.Extensions.AI",
                "Microsoft.Extensions.DependencyInjection")
            .Namespace(ns =>
            {
                ns.Name = namespaceName;
                foreach (var c in commands)
                    BuildHandlerClass(ns, c);
                BuildServiceCollectionExtensions(ns, commands);
            });

        return option.Build();
    }

    private static void BuildHandlerClass(NamespaceOption ns, CommandMethodModel cmd)
    {
        var handlerClassName = GetHandlerClassName(cmd);

        ns.Public.Sealed.Class(cls =>
        {
            cls.WithName(handlerClassName);
            cls.Inherit("ICommandHandler");

            cls.AppendLine($"    public string CommandName => \"{Escape(cmd.CommandName)}\";");

            if (cmd.Aliases.Count > 0)
            {
                var aliasLiterals = string.Join(", ", cmd.Aliases.Select(a => $"\"{Escape(a)}\""));
                cls.AppendLine($"    public string[] Aliases => [{aliasLiterals}];");
            }
            else
            {
                cls.AppendLine("    public string[] Aliases => [];");
            }

            cls.AppendLine($"    public string Description => \"{Escape(cmd.Description)}\";");

            cls.Field(f =>
            {
                f.WithKeyword("private").WithKeyword("readonly")
                    .WithType("IServiceProvider").WithName("_serviceProvider");
            });

            cls.Constructor(ctor =>
            {
                ctor.WithKeyword("public").WithName(handlerClassName)
                    .WithParameter("IServiceProvider serviceProvider");
                ctor.AppendLine("_serviceProvider = serviceProvider;");
            });

            cls.Public.Method(m =>
            {
                m.WithName("ExecuteAsync")
                    .WithReturnType("IAsyncEnumerable<ChatResponseUpdate>")
                    .WithParameters("AgentContext context, ChatResponseUpdateHandler next, CancellationToken ct");

                m.AppendLine($"var instance = (_serviceProvider.GetService(typeof({cmd.ContainingTypeName})) as {cmd.ContainingTypeName})");
                m.AppendLine($"    ?? throw new System.InvalidOperationException(\"Failed to resolve type '{Escape(cmd.ContainingTypeShortName)}' from IServiceProvider.\");");
                m.AppendLine($"return instance.{cmd.MethodName}(context, next, ct);");
            });
        });
    }

    private static void BuildServiceCollectionExtensions(NamespaceOption ns, List<CommandMethodModel> commands)
    {
        ns.Internal.Static.Class(cls =>
        {
            cls.WithName("SlashCommandServiceExtensions");

            cls.Internal.Method(m =>
            {
                m.WithName("AddSlashCommands")
                    .WithReturnType("IServiceCollection")
                    .WithParameters("this IServiceCollection services")
                    .WithKeyword("static");

                foreach (var cmd in commands)
                    m.AppendLine($"services.AddScoped<ICommandHandler, {GetHandlerClassName(cmd)}>();");

                m.AppendLine("services.AddScoped<SlashCommandRegistry>();");   // Scoped:避免 captive dependency(见计划顶部说明)
                m.AppendLine("return services;");
            });
        });
    }

    private static string GetHandlerClassName(CommandMethodModel cmd)
    {
        var typeName = cmd.ContainingTypeName.Replace("<", "_").Replace(">", "_").Replace(".", "_");
        return $"{typeName}_{cmd.MethodName}_CommandHandler";
    }

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}
```

- [ ] **Step 5: Add the generator**

`src/ManInBlack.AI.SourceGenerator/SlashCommandGenerator.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ManInBlack.AI.SourceGenerator;

[Generator]
public sealed class SlashCommandGenerator : IIncrementalGenerator
{
    private const string CommandAttributeFullName = "ManInBlack.AI.Abstraction.Attributes.SlashCommandAttribute";

    private static readonly DiagnosticDescriptor ClassNotPartial = new(
        id: "MIB020",
        title: "包含 [SlashCommand] 方法的类必须声明为 partial",
        messageFormat: "类 '{0}' 包含 [SlashCommand] 方法,必须声明为 partial",
        category: "SlashCommandDeclaration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor EmptyDescription = new(
        id: "MIB021",
        title: "[SlashCommand] 缺少 description",
        messageFormat: "[SlashCommand] '{0}' 的 description 为空",
        category: "SlashCommandDeclaration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateCommand = new(
        id: "MIB022",
        title: "命令名/别名重复",
        messageFormat: "命令名/别名 '{0}' 在 assembly 内重复",
        category: "SlashCommandDeclaration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commandMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetCommandMethodModel(ctx))
            .Where(static m => m is not null)
            .Collect();

        var namespaceProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns) ? ns : "Generated");

        var combined = commandMethods.Combine(namespaceProvider);

        context.RegisterSourceOutput(combined, (spc, source) =>
        {
            var (methods, ns) = source;
            var methodList = methods.Where(m => m is not null).Select(m => m!).ToList();

            if (methodList.Count == 0)
                return;

            ReportDiagnostics(spc, methodList);

            var partialMethods = methodList.Where(m => m.IsPartialClass).ToList();
            if (partialMethods.Count == 0)
                return;

            var sourceText = SlashCommandEmitter.Emit(ns, partialMethods);
            spc.AddSource("SlashCommandHandlers.g.cs", SourceText.From(sourceText, Encoding.UTF8));
        });
    }

    private static CommandMethodModel? GetCommandMethodModel(GeneratorSyntaxContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
        if (methodSymbol is null) return null;

        var attr = methodSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is not null &&
            a.AttributeClass.ToDisplayString() == CommandAttributeFullName);
        if (attr is null) return null;

        var containingType = methodSymbol.ContainingType;
        if (containingType.TypeParameters.Length > 0 && containingType.TypeArguments.Length == 0)
            return null;   // 跳过开放泛型类型

        var fullyQualifiedFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

        var commandName = attr.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value as string ?? methodSymbol.Name
            : methodSymbol.Name;
        var description = attr.ConstructorArguments.Length > 1
            ? attr.ConstructorArguments[1].Value as string ?? ""
            : "";

        var aliases = new List<string>();
        foreach (var na in attr.NamedArguments)
        {
            if (na.Key == "Aliases")
                foreach (var v in na.Value.Values)
                    if (v.Value is string s) aliases.Add(s);
        }

        var classDecl = methodDecl.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        var isPartialClass = classDecl is not null &&
                             classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);

        return new CommandMethodModel
        {
            MethodName = methodSymbol.Name,
            ContainingTypeName = containingType.ToDisplayString(fullyQualifiedFormat),
            ContainingTypeShortName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            CommandName = commandName,
            Description = description,
            Aliases = aliases,
            IsPartialClass = isPartialClass,
        };
    }

    private static void ReportDiagnostics(SourceProductionContext spc, List<CommandMethodModel> methods)
    {
        // MIB020: 非 partial(每个类型只报一次)
        foreach (var group in methods.Where(m => !m.IsPartialClass).GroupBy(m => m.ContainingTypeName))
            spc.ReportDiagnostic(Diagnostic.Create(ClassNotPartial, null, group.First().ContainingTypeShortName));

        // MIB021: 空 description
        foreach (var m in methods.Where(m => string.IsNullOrWhiteSpace(m.Description)))
            spc.ReportDiagnostic(Diagnostic.Create(EmptyDescription, null, m.CommandName));

        // MIB022: 命令名/别名重复
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in methods)
        {
            foreach (var key in new[] { m.CommandName }.Concat(m.Aliases))
                if (!seen.Add(key))
                    spc.ReportDiagnostic(Diagnostic.Create(DuplicateCommand, null, key));
        }
    }
}
```

- [ ] **Step 6: Run the integration test to verify it passes**

Run: `dotnet test test/ManInBlack.AI.Tests/ManInBlack.AI.Tests.csproj --filter "FullyQualifiedName~SlashCommandGeneratorTests"`
Expected: PASS.

> If it fails to find `AddSlashCommands()`, rebuild the source-generator project first so the analyzer picks up the new generator: `dotnet build src/ManInBlack.AI.SourceGenerator/ManInBlack.AI.SourceGenerator.csproj`, then re-run.

- [ ] **Step 7: Commit**

```bash
git add src/ManInBlack.AI.SourceGenerator/CommandMethodModel.cs src/ManInBlack.AI.SourceGenerator/SlashCommandGenerator.cs src/ManInBlack.AI.SourceGenerator/SlashCommandEmitter.cs test/ManInBlack.AI.Tests/Commands/SlashCommandGeneratorTests.cs
git commit -m "Add SlashCommandGenerator: discovery, handlers, AddSlashCommands, diagnostics"
```

---

### Task 8: Wire into DI + pipeline, migrate `/new` out, full verification

**Files:**
- Modify: `src/ManInBlack.AI/DependencyInjection.cs:121`
- Modify: `src/ManInBlack.AI/AgentPipelines.cs:23-31`
- Modify: `src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs:68-92`
- Modify: `test/ManInBlack.AI.Tests/Middlewares/PersistenceMiddlewareTests.cs`

- [ ] **Step 1: Wire `AddSlashCommands()` into DI**

In `src/ManInBlack.AI/DependencyInjection.cs`, change line 121 from:

```csharp
            services.AddToolHandlers();
```

to:

```csharp
            services.AddToolHandlers();
            services.AddSlashCommands();
```

- [ ] **Step 2: Insert `CommandMiddleware` into the pipeline**

In `src/ManInBlack.AI/AgentPipelines.cs`, inside `UseDefault(...)`, change:

```csharp
        builder
            .Use<EventPublishingMiddleware>() // 在最外层, 用于ui监听agent事件
            .Use<ReadPersistenceMiddleware>()
```

to:

```csharp
        builder
            .Use<EventPublishingMiddleware>() // 在最外层, 用于ui监听agent事件
            .Use<CommandMiddleware>()         // 拦截 / 命令,在持久化之前短路
            .Use<ReadPersistenceMiddleware>()
```

- [ ] **Step 3: Delete the command block from `ReadPersistenceMiddleware`**

In `src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs`, delete the entire block from the comment `// 重置对话 command` through its closing brace (the `if (UserInputCommandHelper.FetchCommand(...)) { ... }` block, lines 68-92). After deletion, also remove the now-unused `using ManInBlack.AI.Services;` at the top of the file if the compiler reports it as unused (it was only used for `UserInputCommandHelper`).

The surrounding code should read directly from the `SaveCheckpoint` callback block into the history-loading block:

```csharp
        // ... SaveCheckpoint 回调注入结束 ...

        var messages = await sessionStorage.LoadMessages(context.SessionId); // 从workspace 里获取的消息...
```

- [ ] **Step 4: Delete the 3 obsolete command tests**

In `test/ManInBlack.AI.Tests/Middlewares/PersistenceMiddlewareTests.cs`, delete these three test methods entirely (their behavior is now covered by `CommandMiddlewareTests` + `BuiltinCommandsTests`):

- `HandleAsync_ClearCommand_ShouldResetAndYieldConfirmation`
- `HandleAsync_ResetCommand_ShouldAlsoReset`
- `HandleAsync_NewCommand_ShouldAlsoReset`

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build ManInBlack.sln` (or `dotnet build` from repo root)
Expected: Build succeeded. No errors. (Warnings about LF→CRLF are fine.)

> If you hit `MIB020` on `BuiltinCommands`, confirm the class is declared `partial` (it is, in Task 6). If you hit `MIB022`, two commands share a name/alias — fix the attribute.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test ManInBlack.sln`
Expected: All tests PASS — including `SlashCommandRegistryTests`, `SlashCommandItemsTests`, `CommandHookModelTests`, `CommandMiddlewareTests`, `BuiltinCommandsTests`, `SlashCommandGeneratorTests`, and the remaining `ReadPersistenceMiddlewareTests` / `SavePersistenceMiddlewareTests`.

- [ ] **Step 7: Commit**

```bash
git add src/ManInBlack.AI/DependencyInjection.cs src/ManInBlack.AI/AgentPipelines.cs src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs test/ManInBlack.AI.Tests/Middlewares/PersistenceMiddlewareTests.cs
git commit -m "Wire CommandMiddleware into pipeline, migrate /new out of persistence"
```

---

## Verification Checklist (post-Task 8)

- `/new`, `/clear`, `/reset` all reset the session (new SessionId, cleared messages, confirmation) — covered by `BuiltinCommandsTests` + `CommandMiddlewareTests`.
- `/help` lists all registered commands — covered by `BuiltinCommandsTests`.
- Unknown `/xxx` yields the hint and short-circuits — covered by `CommandMiddlewareTests`.
- Non-`/` input passes through to the LLM pipeline — covered by `CommandMiddlewareTests`.
- `CommandExecutedEvent` fires on the observer lane with correct payload — covered by `CommandMiddlewareTests`.
- `AfterCommand` script hook fires (even for short-circuit commands) — covered by `CommandMiddlewareTests` (FakeHookExecutor).
- Generator discovers all `[SlashCommand]` methods and registers them — covered by `SlashCommandGeneratorTests`.
- `ReadPersistenceMiddleware` no longer contains any command logic — verified by deletion in Task 8 + remaining non-command tests still pass.
