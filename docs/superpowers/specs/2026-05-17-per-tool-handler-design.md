# Per-Tool Handler 设计

> 日期：2026-05-17
> 状态：已批准

## 背景

源生成器为每个项目生成一个单体 `ToolExecutor`（switch 语句），但源生成器只能看到当前项目的代码。当 demo 项目定义自定义工具时，生成的 `ToolExecutor` 只包含自己的工具，无法与 `ManInBlack.AI` 内置工具共存。DI 容器只能注册一个 `IToolExecutor` 实现。

## 方案

将单体 `ToolExecutor` 拆分为**独立的 per-tool handler**，通过 DI 自动组合跨项目的工具。

## 核心接口

在 `ManInBlack.AI.Abstraction/Tools/` 中新增：

```csharp
public interface IToolHandler
{
    string ToolName { get; }
    Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct = default);
}
```

## 源生成器改造

`ToolCallerGenerator` → 输出 `ToolHandlers.g.cs`，包含：

1. **Per-tool handler 类**：每个 `[AiTool]` 方法生成一个 `IToolHandler` 实现
2. **ToolExecutor**：字典查找版本，从 DI 收集所有 handler
3. **AddToolHandlers()**：DI 注册扩展方法

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

### ToolExecutor

```csharp
public sealed class ToolExecutor : IToolExecutor
{
    private readonly Dictionary<string, IToolHandler> _handlers;

    public ToolExecutor(IEnumerable<IToolHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.ToolName);
    }

    public async Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(ctx.ToolName, out var handler))
            throw new ArgumentException($"Unknown tool: '{ctx.ToolName}'.");
        await handler.ExecuteAsync(ctx, ct);
    }
}
```

### DI 注册

```csharp
internal static IServiceCollection AddToolHandlers(this IServiceCollection services)
{
    services.AddScoped<IToolHandler, FileTools_Read_Handler>();
    services.AddScoped<IToolHandler, FileTools_Write_Handler>();
    // ... 所有 handler
    services.AddScoped<IToolExecutor, ToolExecutor>();
    return services;
}
```

## 调用方变化

- `DependencyInjection.cs`：`services.AddToolExecutor()` → `services.AddToolHandlers()`
- 所有引用源生成器的项目重新编译即可

## Pipeline 级别控制

利用现有的 ToolMiddleware 机制（per-tool-class middleware 注入声明），不在 executor 层做额外过滤。

- 工具声明：pipeline 中的 ToolMiddleware 控制哪些工具对 LLM 可见
- 工具执行：统一的 ToolExecutor 包含所有 handler，按名字分发

## 同名工具冲突

与当前 `ResolveToolNames` 逻辑一致：不同类中的同名方法会加上类名前缀（`ClassName.MethodName`），handler 名字也包含类名（`ClassName_MethodName_Handler`）。

## 涉及文件

| 文件 | 变更 |
|------|------|
| `ManInBlack.AI.Abstraction/Tools/IToolHandler.cs` | 新增 |
| `ManInBlack.AI.SourceGenerator/ToolCallerEmitter.cs` | 重写，生成 per-tool handler |
| `ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs` | 小幅调整，传递已有 model |
| `ManInBlack.AI/DependencyInjection.cs` | `AddToolExecutor()` → `AddToolHandlers()` |
| `ManInBlack.AI.SourceGenerator/ToolCallerModel.cs` | 无变化 |
| `test/ManInBlack.AI.Tests/` | 更新测试用例 |
| `docs/tools-guide.md` | 更新文档 |
| `docs/sourcegenerator-guide.md` | 更新文档 |
