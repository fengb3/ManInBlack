# Per-Tool Handler + ToolRegistry 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将单体 ToolExecutor 拆分为 per-tool handler，新增 ToolRegistry 集中管理工具声明，支持跨项目工具组合。

**Architecture:** IToolHandler 替代单体 switch executor，IToolDeclaration + ToolRegistry 替代 per-class AllToolDeclarations + per-class middleware。源生成器为每个 [AiTool] 方法生成独立的 handler 和 declaration，通过 DI 自动组合。

**Tech Stack:** C# 13, Roslyn Source Generator, Fengb3.EasyCodeBuilder, Microsoft.Extensions.AI, Microsoft.Extensions.DependencyInjection

---

### Task 1: 新增核心接口（Abstraction 层）

**Files:**
- Create: `src/ManInBlack.AI.Abstraction/Tools/IToolHandler.cs`
- Create: `src/ManInBlack.AI.Abstraction/Tools/IToolDeclaration.cs`

- [ ] **Step 1: 创建 IToolHandler.cs**

```csharp
namespace ManInBlack.AI.Abstraction.Tools;

public interface IToolHandler
{
    string ToolName { get; }
    Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct = default);
}
```

- [ ] **Step 2: 创建 IToolDeclaration.cs**

```csharp
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Tools;

public interface IToolDeclaration
{
    string ToolName { get; }
    string Group { get; }
    AIFunctionDeclaration Declaration { get; }
}
```

- [ ] **Step 3: 构建 Abstraction 项目验证编译**

Run: `dotnet build src/ManInBlack.AI.Abstraction`
Expected: 成功

- [ ] **Step 4: Commit**

```
feat: 新增 IToolHandler 和 IToolDeclaration 接口
```

---

### Task 2: 新增 ToolDeclaration、ToolRegistry、ToolExecutor、ToolsMiddleware（主库）

**Files:**
- Create: `src/ManInBlack.AI/Tools/ToolDeclaration.cs`
- Create: `src/ManInBlack.AI/Tools/ToolRegistry.cs`
- Create: `src/ManInBlack.AI/Tools/ToolExecutor.cs`
- Create: `src/ManInBlack.AI/Middlewares/ToolsMiddleware.cs`

- [ ] **Step 1: 创建 ToolDeclaration.cs**

```csharp
using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tools;

public sealed class ToolDeclaration(string toolName, string group, AIFunctionDeclaration declaration)
    : IToolDeclaration
{
    public string ToolName => toolName;
    public string Group => group;
    public AIFunctionDeclaration Declaration => declaration;
}
```

- [ ] **Step 2: 创建 ToolRegistry.cs**

```csharp
using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tools;

public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, IToolDeclaration> _declarations;

    public ToolRegistry(IEnumerable<IToolDeclaration> declarations)
    {
        _declarations = new(declarations.ToDictionary(d => d.ToolName));
    }

    public IReadOnlyList<AIFunctionDeclaration> GetAll()
        => _declarations.Values.Select(d => d.Declaration).ToList();

    public IReadOnlyList<AIFunctionDeclaration> GetByGroups(params string[] groups)
        => _declarations.Values
            .Where(d => groups.Contains(d.Group))
            .Select(d => d.Declaration)
            .ToList();

    public void Register(IToolDeclaration declaration)
        => _declarations[declaration.ToolName] = declaration;
}
```

- [ ] **Step 3: 创建 ToolExecutor.cs**

```csharp
using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Tools;

namespace ManInBlack.AI.Tools;

public sealed class ToolExecutor : IToolExecutor
{
    private readonly ConcurrentDictionary<string, IToolHandler> _handlers;

    public ToolExecutor(IEnumerable<IToolHandler> handlers)
    {
        _handlers = new(handlers.ToDictionary(h => h.ToolName));
    }

    public void Register(IToolHandler handler)
        => _handlers[handler.ToolName] = handler;

    public async Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(ctx.ToolName, out var handler))
            throw new ArgumentException($"Unknown tool: '{ctx.ToolName}'.");
        await handler.ExecuteAsync(ctx, ct);
    }
}
```

- [ ] **Step 4: 创建 ToolsMiddleware.cs**

```csharp
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace ManInBlack.AI.Middlewares;

public class ToolsMiddleware : AgentMiddleware
{
    private readonly ToolRegistry _registry;
    private readonly string[]? _groups;

    public ToolsMiddleware(ToolRegistry registry)
    {
        _registry = registry;
    }

    public ToolsMiddleware(ToolRegistry registry, string[] groups)
    {
        _registry = registry;
        _groups = groups;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context, ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        context.Options ??= new ChatOptions();
        context.Options.Tools ??= [];

        var declarations = _groups is null
            ? _registry.GetAll()
            : _registry.GetByGroups(_groups);

        foreach (var d in declarations)
            context.Options.Tools!.Add(d);

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }
}
```

- [ ] **Step 5: 构建验证**

Run: `dotnet build src/ManInBlack.AI`
Expected: 成功（源生成器还会生成旧的 ToolExecutor，可能冲突，先忽略）

- [ ] **Step 6: Commit**

```
feat: 新增 ToolDeclaration、ToolRegistry、ToolExecutor、ToolsMiddleware
```

---

### Task 3: 重写 ToolCallerEmitter — 生成 per-tool handler + declaration 注册

**Files:**
- Rewrite: `src/ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs`

- [ ] **Step 1: 重写 ToolCallerEmitter.Emit 方法**

核心变化：
- 为每个 `[AiTool]` 方法生成独立的 `IToolHandler` 实现类（复用现有 `BuildCoreInvocationLines`、`BuildParameterExtraction`、`BuildFilterPipelineLines` 逻辑）
- 生成 `AddToolHandlers()` 扩展方法，注册所有 `IToolHandler`、`IToolDeclaration`、`ToolExecutor`、`ToolRegistry`
- 生成 `ToolDeclaration` 注册代码（复用 `ToolDeclarationEmitter` 的 JSON Schema 生成逻辑）

handler 类名格式：`{TypeName}_{MethodName}_Handler`
handler 的 `ToolName` 使用 `tool.ToolName`（已处理冲突）

- [ ] **Step 2: 构建源生成器验证**

Run: `dotnet build src/ManInBlack.AI.SourceGenerator`
Expected: 成功

- [ ] **Step 3: Commit**

```
refactor: 重写 ToolCallerEmitter 生成 per-tool handler + declaration
```

---

### Task 4: 更新 ToolCallerGenerator — 合并声明生成逻辑

**Files:**
- Modify: `src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs`

- [ ] **Step 1: 合并 ToolDeclarationGenerator 的 model 提取逻辑到 ToolCallerGenerator**

当前 `ToolCallerGenerator` 只提取 `ToolMethodModel`（不含 XML 文档）。需要扩展 `ToolMethodModel` 或合并 `ToolDeclarationModel` 的字段，让生成的 handler 同时拥有执行信息和声明信息（summary、paramDescriptions、returnsDescription）。

具体：
- 在 `ToolMethodModel` 中新增 `Summary`、`ParamDescriptions`、`ReturnsDescription`、`ContainingNamespace` 字段
- 在 `ToolCallerGenerator.GetToolMethodModel()` 中提取 XML 文档（复用 `ToolDeclarationGenerator` 的 `ExtractXmlDoc` 逻辑）
- 传递完整的 model 列表给 `ToolCallerEmitter.Emit()`

- [ ] **Step 2: 迁移诊断规则**

将 MIB010（非 partial）、MIB011（缺 summary）、MIB012（缺 param）、MIB013（缺 returns）诊断规则从 `ToolDeclarationGenerator` 迁移到 `ToolCallerGenerator`。

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/ManInBlack.AI.SourceGenerator`
Expected: 成功

- [ ] **Step 4: Commit**

```
refactor: 合并声明生成逻辑到 ToolCallerGenerator
```

---

### Task 5: 删除旧生成器 + 更新 DI 注册

**Files:**
- Delete: `src/ManInBlack.AI.SourceGenerator/ToolDeclarationGenerator.cs`
- Delete: `src/ManInBlack.AI.SourceGenerator/ToolDeclarationEmitter.cs`
- Delete: `src/ManInBlack.AI.SourceGenerator/ToolDeclarationModel.cs`
- Delete: `src/ManInBlack.AI.SourceGenerator/ToolMiddlewareGenerator.cs`
- Delete: `src/ManInBlack.AI.SourceGenerator/ToolMiddlewareEmitter.cs`
- Delete: `src/ManInBlack.AI.SourceGenerator/ToolMiddlewareModel.cs`
- Modify: `src/ManInBlack.AI/DependencyInjection.cs`

- [ ] **Step 1: 删除 6 个旧文件**

- [ ] **Step 2: 更新 DependencyInjection.cs**

将 `services.AddToolExecutor()` + `services.AddToolMiddlewares()` 替换为 `services.AddToolHandlers()`。

- [ ] **Step 3: 更新 AgentPipelines.cs**

将 per-class middleware（`Use<FileToolsMiddleware>()`、`Use<CommandLineToolsMiddleware>()` 等）替换为 `Use<ToolsMiddleware>()`。

- [ ] **Step 4: 全量构建验证**

Run: `dotnet build ManInBlack.slnx`
Expected: 成功

- [ ] **Step 5: Commit**

```
refactor: 删除旧生成器，更新 DI 和 Pipeline 配置
```

---

### Task 6: 更新测试

**Files:**
- Modify: `test/ManInBlack.AI.Tests/Helpers/FakeToolExecutor.cs`
- Update other test files as needed

- [ ] **Step 1: 确认 FakeToolExecutor 仍然兼容**

`FakeToolExecutor` 实现 `IToolExecutor`，新的 `ToolExecutor` 也实现 `IToolExecutor`，接口未变，应兼容。检查是否有直接引用旧 `ToolExecutor` 类名的测试。

- [ ] **Step 2: 运行测试**

Run: `dotnet test test/ManInBlack.AI.Tests`
Expected: 全部通过

- [ ] **Step 3: Commit**

```
test: 更新测试适配新架构
```

---

### Task 7: 更新文档

**Files:**
- Modify: `docs/tools-guide.md`
- Modify: `docs/sourcegenerator-guide.md`

- [ ] **Step 1: 更新 tools-guide.md**

更新"编写自定义工具"章节，说明新的 handler + declaration 机制。更新 ToolCallFilter 管道说明。

- [ ] **Step 2: 更新 sourcegenerator-guide.md**

更新源生成器说明，反映 3 → 1 生成器的变化。

- [ ] **Step 3: Commit**

```
docs: 更新工具和源生成器文档
```

---

### Task 8: 全量验证 + 创建 PR

- [ ] **Step 1: 全量构建**

Run: `dotnet build ManInBlack.slnx`

- [ ] **Step 2: 全量测试**

Run: `dotnet test ManInBlack.slnx`

- [ ] **Step 3: 推送分支 + 创建 PR**
