# Per-Tool Handler + ToolRegistry 设计

> 日期：2026-05-17
> 状态：已批准

## 背景

源生成器为每个项目生成一个单体 `ToolExecutor`（switch 语句），但源生成器只能看到当前项目的代码。当 demo 项目定义自定义工具时，生成的 `ToolExecutor` 只包含自己的工具，无法与 `ManInBlack.AI` 内置工具共存。DI 容器只能注册一个 `IToolExecutor` 实现。

同理，工具声明也分散在每个工具类的 `AllToolDeclarations` 静态字段和 per-class ToolMiddleware 中，缺乏集中式管理。

## 方案

将单体 `ToolExecutor` 拆分为**独立的 per-tool handler**，将分散的工具声明集中到 **ToolRegistry**，均通过 DI 自动组合跨项目的工具。

## 核心接口

在 `ManInBlack.AI.Abstraction/Tools/` 中新增：

```csharp
public interface IToolHandler
{
    string ToolName { get; }
    Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct = default);
}

public interface IToolDeclaration
{
    string ToolName { get; }
    string Group { get; }
    AIFunction Declaration { get; }
}

public sealed class ToolDeclaration(string toolName, string group, AIFunction declaration)
    : IToolDeclaration
{
    public string ToolName => toolName;
    public string Group => group;
    public AIFunction Declaration => declaration;
}
```

## ToolRegistry

集中管理所有工具声明，通过 DI 收集所有 `IToolDeclaration`：

```csharp
public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, IToolDeclaration> _declarations;

    public ToolRegistry(IEnumerable<IToolDeclaration> declarations)
    {
        _declarations = new(declarations.ToDictionary(d => d.ToolName));
    }

    public IReadOnlyList<AIFunction> GetAll()
        => _declarations.Values.Select(d => d.Declaration).ToList();

    public IReadOnlyList<AIFunction> GetByGroups(params string[] groups)
        => _declarations.Values
            .Where(d => groups.Contains(d.Group))
            .Select(d => d.Declaration)
            .ToList();

    public void Register(IToolDeclaration declaration)
        => _declarations[declaration.ToolName] = declaration;
}
```

## ToolExecutor

从单体 switch 改为字典查找：

```csharp
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

## ToolsMiddleware

替换所有 per-class ToolMiddleware，统一从 `ToolRegistry` 获取声明：

```csharp
public class ToolsMiddleware : AgentMiddleware
{
    private readonly ToolRegistry _registry;
    private readonly string[]? _groups;

    // 注入所有工具
    public ToolsMiddleware(ToolRegistry registry)
    {
        _registry = registry;
    }

    // 按组过滤
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

## 源生成器改造

### 删除的生成器

- `ToolMiddlewareGenerator` — 由统一的 `ToolsMiddleware` 替代
- `ToolDeclarationGenerator` — 声明注册合并进 handler 生成器

### 改造的生成器

`ToolCallerGenerator` → 输出 `ToolHandlers.g.cs`，包含：

1. **Per-tool handler 类**：每个 `[AiTool]` 方法生成一个 `IToolHandler` 实现
2. **Per-tool declaration 注册**：每个方法同时注册 `IToolDeclaration`
3. **ToolExecutor**：字典查找版本
4. **AddToolHandlers()**：DI 注册扩展方法，同时注册 handler + declaration + executor

### Handler 类示例

```csharp
public sealed class FileTools_Read_Handler : IToolHandler
{
    private readonly IServiceProvider _serviceProvider;
    public string ToolName => "Read";

    public FileTools_Read_Handler(IServiceProvider sp) => _serviceProvider = sp;

    public async Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct)
    {
        // 参数提取 + 类型转换 + 实例解析 + 方法调用
        // 与当前 ToolExecutor helper 方法逻辑一致
    }
}
```

### Declaration 注册示例

源生成器同时生成声明对象（复用当前 `ToolDeclarationEmitter` 的 JSON Schema 生成逻辑）：

```csharp
// 在 AddToolHandlers() 中
services.AddSingleton<IToolDeclaration>(new ToolDeclaration("Read", "FileTools", Read_Declaration));
services.AddSingleton<IToolDeclaration>(new ToolDeclaration("Write", "FileTools", Write_Declaration));
```

### DI 注册

```csharp
internal static IServiceCollection AddToolHandlers(this IServiceCollection services)
{
    // Handler
    services.AddScoped<IToolHandler, FileTools_Read_Handler>();
    services.AddScoped<IToolHandler, FileTools_Write_Handler>();
    // ... 所有 handler

    // Declaration
    services.AddSingleton<IToolDeclaration>(new ToolDeclaration("Read", "FileTools", Read_Declaration));
    services.AddSingleton<IToolDeclaration>(new ToolDeclaration("Write", "FileTools", Write_Declaration));
    // ... 所有 declaration

    // Registry + Executor
    services.AddSingleton<ToolRegistry>();
    services.AddScoped<IToolExecutor, ToolExecutor>();
    return services;
}
```

## 调用方变化

- `DependencyInjection.cs`：`services.AddToolExecutor()` → `services.AddToolHandlers()`
- `DependencyInjection.cs`：`services.AddToolMiddlewares()` → 删除
- Pipeline 配置不再需要 per-class middleware，改为：

```csharp
// 全部工具
builder.Use<ToolsMiddleware>()

// 按组选择
builder.Use(sp => new ToolsMiddleware(sp.GetRequiredService<ToolRegistry>(), ["FileTools", "MyTools"]))
```

- 所有引用源生成器的项目重新编译即可

## Pipeline 级别控制

通过 `ToolsMiddleware` 的 `groups` 参数过滤声明。

- 工具声明：`ToolRegistry` 集中管理，`ToolsMiddleware` 按 group 选择注入
- 工具执行：统一的 `ToolExecutor` 包含所有 handler，按名字分发

## 同名工具冲突

与当前 `ResolveToolNames` 逻辑一致：不同类中的同名方法会加上类名前缀（`ClassName.MethodName`），handler 名字也包含类名（`ClassName_MethodName_Handler`），declaration 的 group 为类名。

## 整体架构

| 组件 | 职责 | DI 注入 |
|------|------|---------|
| `ToolRegistry` | 声明集中管理 | `IEnumerable<IToolDeclaration>` |
| `ToolExecutor` | 执行集中分发 | `IEnumerable<IToolHandler>` |
| `ToolsMiddleware` | 按 group 选择注入声明 | 引用 `ToolRegistry` |
| `ToolDeclaration` | 单个工具声明 | name + group + AIFunction |

## 涉及文件

| 文件 | 变更 |
|------|------|
| `ManInBlack.AI.Abstraction/Tools/IToolHandler.cs` | 新增 |
| `ManInBlack.AI.Abstraction/Tools/IToolDeclaration.cs` | 新增 |
| `ManInBlack.AI.Abstraction/Tools/ToolDeclaration.cs` | 新增 |
| `ManInBlack.AI/Tools/ToolRegistry.cs` | 新增 |
| `ManInBlack.AI/Tools/ToolsMiddleware.cs` | 新增 |
| `ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs` | 重写，生成 per-tool handler + declaration 注册 |
| `ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs` | 合并 declaration 生成逻辑 |
| `ManInBlack.AI.SourceGenerator/ToolDeclarationGenerator.cs` | 删除，合并进 ToolCallerGenerator |
| `ManInBlack.AI.SourceGenerator/ToolDeclarationEmitter.cs` | 合并进 ToolCallerEmitter |
| `ManInBlack.AI.SourceGenerator/ToolMiddlewareGenerator.cs` | 删除 |
| `ManInBlack.AI.SourceGenerator/ToolMiddlewareEmitter.cs` | 删除 |
| `ManInBlack.AI/DependencyInjection.cs` | `AddToolExecutor()` + `AddToolMiddlewares()` → `AddToolHandlers()` |
| `ManInBlack.AI/AgentPipelines.cs` | per-class middleware → `ToolsMiddleware` |
| `test/ManInBlack.AI.Tests/` | 更新测试用例 |
| `docs/tools-guide.md` | 更新文档 |
| `docs/sourcegenerator-guide.md` | 更新文档 |
