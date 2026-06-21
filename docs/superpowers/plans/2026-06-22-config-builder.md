# 配置 Builder 改造 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 ManInBlack 增加流式 Builder API（`services.AddManInBlack().AddProvider(...).AddAgent(...).UseJson()...`），让委托与 JSON 统一到同一个 `ManInBlackSettings` 内部模型、按注册顺序合并，并把 pipeline 注册收进 DI 期。

**Architecture:** Builder 每次流式调用翻译成一条 `IManInBlackContribution` 单例写进 `IServiceCollection`；一个 `ManInBlackSettingsBuilder : IConfigureOptions<ManInBlackSettings>` 收集全部贡献，在 `IOptions<ManInBlackSettings>` 首次 resolve 时按序合并，复用现有 `ValidateManInBlackSettings` 校验。凡是 `AgentFactory` ctor 通过 `IEnumerable<T>` 收集的类型（`AgentDefinition`、`PipelineRegistration`）由 builder 方法**即时注册**单例（ServiceProvider 构建前必须就位）；其余配置走懒合并贡献。旧三个 DI 入口保留为薄封装，`AddManInBlack(Action<ManInBlackOptions>)` 标 `[Obsolete]`。

**Tech Stack:** .NET 10、C# 13、`Microsoft.Extensions.DependencyInjection` / `Microsoft.Extensions.Options` / `Microsoft.Extensions.Configuration`、xunit（手写 fake）。

## Global Constraints

- 目标框架 `net10.0`；`ImplicitUsings` 与 `Nullable` 均启用。
- DI 扩展类 `DependencyInjection` 仍用 C# 13 `extension(IServiceCollection services)` 语法；新增的流式 builder 扩展方法用传统 `public static T M(this ...)` 静态类（避免与 `extension` 块混用的歧义）。
- 所有代码注释与文档使用**中文**。
- 测试用**手写 fake**，不引入 mock 框架（`FeishuAdaptor.Tests` 除外）。
- 提交信息用 [gitmoji](https://gitmoji.dev/) 前缀，**禁止** `Co-authored-by` 尾部。
- 修改模块后必须同步更新 `docs/` 下对应文档（配置/快速开始/agent 工厂）。
- 本计划**不涉及**源生成器（`[AiTool]`）与 `AgentLoopMiddleware` 最内层约束。

---

## File Structure

新建（`src/ManInBlack.AI/`）：
- `Configuration/IManInBlackContribution.cs` — 贡献接口（internal）。
- `Configuration/ActionContribution.cs` — 把 `Action<ManInBlackSettings>` 包成贡献。
- `Configuration/SettingsMerger.cs` — 全量 settings 的按 key 合并（供 UseJson/UseConfiguration）。
- `Configuration/ManInBlackSettingsBuilder.cs` — 收集贡献、实现 `IConfigureOptions<ManInBlackSettings>`。
- `Configuration/PipelineRegistration.cs` — `{ Name, Resolver }` 记录。
- `Configuration/SubBuilders/ProviderBuilder.cs`、`ModelChoiceBuilder.cs`、`AgentBuilder.cs`、`HookBuilder.cs`、`McpServerBuilder.cs`、`StorageBuilder.cs` — 流式子 builder。
- `ManInBlackBuilder.cs` — `IManInBlackBuilder` 实现（internal sealed），持有 `Services` 与 `internal AddContribution`。
- `IManInBlackBuilder.cs` — public 接口，仅暴露 `Services`。
- `ManInBlackBuilderExtensions.cs` — 流式扩展方法（AddProvider/AddAgent/AddPipeline/UseJson/UseSandbox 等）。

新建（`demo/FeishuAdaptor/`）：
- `FeishuBuilderExtensions.cs` — `AddFeishu(this IManInBlackBuilder, Action<FeishuSettings>)`。

修改（`src/ManInBlack.AI/`）：
- `DependencyInjection.cs` — 新增无参 `AddManInBlack()` 返回 builder；旧三入口改写为薄封装，`AddManInBlack(Action<ManInBlackOptions>)` 标 `[Obsolete]`。
- `AgentFactory.cs` — ctor 增加 `IEnumerable<PipelineRegistration>`，合并进内置 `default`/`simple`。
- `Configuration/ManInBlackConfigurationBuilder.cs` — 新增 `LoadSettings()`（`EnsureSettingsFile` + 读文件绑定到 `ManInBlackSettings`），供 `UseJson()` 复用。

修改（`test/ManInBlack.AI.Tests/`）：
- `AgentFactoryTests.cs` — `CreateFactory()` 适配新 ctor 参数。
- 新增 `Configuration/ManInBlackBuilderTests.cs`（多任务逐步填充）。

修改（`demo/`）：`AgentConsole/Program.cs`、`FeishuAdaptor/Program.cs`、`GitHubAdaptor/Program.cs` 迁移到新 API。

修改（`docs/`）：`configuration-guide.md`、`quick-start.md`、`agent-factory-guide.md`。

---

## Task 1: 合并引擎核心（贡献接口 + 合并器 + 配置 builder + InternalsVisibleTo）

**Files:**
- Create: `src/ManInBlack.AI/Configuration/IManInBlackContribution.cs`
- Create: `src/ManInBlack.AI/Configuration/ActionContribution.cs`
- Create: `src/ManInBlack.AI/Configuration/SettingsMerger.cs`
- Create: `src/ManInBlack.AI/Configuration/ManInBlackSettingsBuilder.cs`
- Create: `src/ManInBlack.AI/Configuration/PipelineRegistration.cs`
- Modify: `src/ManInBlack.AI/ManInBlack.AI.csproj`（加 `InternalsVisibleTo`）
- Test: `test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs`

**Interfaces:**
- Produces（后续任务依赖）：
  - `internal interface IManInBlackContribution { void Apply(ManInBlackSettings settings); }`
  - `internal sealed class ActionContribution(Action<ManInBlackSettings>) : IManInBlackContribution`
  - `internal static class SettingsMerger { static void Merge(ManInBlackSettings target, ManInBlackSettings source); }`
  - `internal sealed class ManInBlackSettingsBuilder : IConfigureOptions<ManInBlackSettings>`，ctor 取 `IEnumerable<IManInBlackContribution>`，`Configure` 按序 `Apply`。
  - `internal sealed record PipelineRegistration(string Name, Func<AgentPipelineBuilder, AgentPipelineBuilder> Resolver);`

- [ ] **Step 1: 加 InternalsVisibleTo**

修改 `src/ManInBlack.AI/ManInBlack.AI.csproj`，在 `</Project>` 前加：

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="ManInBlack.AI.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: 写失败测试**

创建 `test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs`：

```csharp
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class ManInBlackBuilderTests
{
    /// <summary>
    /// 从已注册的 IManInBlackContribution 直接合并出 ManInBlackSettings，跳过完整 DI。
    /// </summary>
    internal static ManInBlackSettings Merge(IServiceCollection services)
    {
        var contributions = services.BuildServiceProvider().GetServices<IManInBlackContribution>();
        var settings = new ManInBlackSettings();
        new ManInBlackSettingsBuilder(contributions).Configure(settings);
        return settings;
    }

    [Fact]
    public void Merge_Dict_LastWriteWinsByKey()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Providers["a"] = new ProviderSettings { Schema = "OpenAI", ApiKey = "old" }));
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Providers["a"] = new ProviderSettings { Schema = "OpenAI", ApiKey = "new" }));
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Providers["b"] = new ProviderSettings { Schema = "Anthropic", ApiKey = "kb" }));

        var settings = Merge(services);

        Assert.Equal("new", settings.Providers["a"].ApiKey);
        Assert.Equal("Anthropic", settings.Providers["b"].Schema);
        Assert.Equal(2, settings.Providers.Count);
    }

    [Fact]
    public void Merge_Hooks_Accumulate()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Hooks.Add(new HookSettings())));
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.Hooks.Add(new HookSettings())));

        var settings = Merge(services);

        Assert.Equal(2, settings.Hooks.Count);
    }

    [Fact]
    public void Merge_Scalar_LastWriteWins()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.UseSandbox = false));
        services.AddSingleton<IManInBlackContribution>(new ActionContribution(s => s.UseSandbox = true));

        var settings = Merge(services);

        Assert.True(settings.UseSandbox);
    }

    [Fact]
    public void SettingsMerger_FullSource_MergesByKey()
    {
        var target = new ManInBlackSettings();
        target.Providers["existing"] = new ProviderSettings { Schema = "OpenAI", ApiKey = "keep" };

        var source = new ManInBlackSettings();
        source.Providers["existing"] = new ProviderSettings { Schema = "OpenAI", ApiKey = "override" };
        source.Providers["new"] = new ProviderSettings { Schema = "Gemini", ApiKey = "kn" };
        source.UseSandbox = true;

        SettingsMerger.Merge(target, source);

        Assert.Equal("override", target.Providers["existing"].ApiKey);
        Assert.True(target.Providers.ContainsKey("new"));
        Assert.True(target.UseSandbox);
    }
}
```

- [ ] **Step 3: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests"`
Expected: FAIL，编译错误（`IManInBlackContribution`/`ActionContribution`/`SettingsMerger`/`ManInBlackSettingsBuilder` 未定义）。

- [ ] **Step 4: 实现 IManInBlackContribution + ActionContribution**

创建 `src/ManInBlack.AI/Configuration/IManInBlackContribution.cs`：

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 一条对 ManInBlackSettings 的配置贡献。在 IOptions 首次 resolve 时按注册顺序应用。
/// </summary>
internal interface IManInBlackContribution
{
    void Apply(ManInBlackSettings settings);
}
```

创建 `src/ManInBlack.AI/Configuration/ActionContribution.cs`：

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 把一个委托包装成贡献。供流式 AddXxx 方法使用。
/// </summary>
internal sealed class ActionContribution(Action<ManInBlackSettings> action) : IManInBlackContribution
{
    public void Apply(ManInBlackSettings settings) => action(settings);
}
```

- [ ] **Step 5: 实现 SettingsMerger**

创建 `src/ManInBlack.AI/Configuration/SettingsMerger.cs`：

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 把一份完整的 source ManInBlackSettings 按 key 合并进 target，供 UseJson/UseConfiguration 使用。
/// 字典按 key 覆盖（保留 target 中 source 没有的 key）；Hooks 累加；标量后者覆盖；Feishu 仅在非空时覆盖。
/// </summary>
internal static class SettingsMerger
{
    public static void Merge(ManInBlackSettings target, ManInBlackSettings source)
    {
        foreach (var kv in source.Providers)
            target.Providers[kv.Key] = kv.Value;

        foreach (var kv in source.ModelChoices)
            target.ModelChoices[kv.Key] = kv.Value;

        foreach (var kv in source.Agents)
            target.Agents[kv.Key] = kv.Value;

        foreach (var kv in source.McpServers)
            target.McpServers[kv.Key] = kv.Value;

        target.Hooks.AddRange(source.Hooks);

        // Storage：仅当 source 有实质内容（非全默认）时覆盖
        if (source.Storage is { } storage && (storage.RootPath is not null || storage.Workspace is not null))
            target.Storage = storage;

        target.UseSandbox = source.UseSandbox;

        if (source.Feishu is not null)
            target.Feishu = source.Feishu;
    }
}
```

- [ ] **Step 6: 实现 ManInBlackSettingsBuilder**

创建 `src/ManInBlack.AI/Configuration/ManInBlackSettingsBuilder.cs`：

```csharp
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 收集全部 IManInBlackContribution，在 IOptions&lt;ManInBlackSettings&gt; 首次 resolve 时按注册顺序合并。
/// </summary>
internal sealed class ManInBlackSettingsBuilder(IEnumerable<IManInBlackContribution> contributions)
    : IConfigureOptions<ManInBlackSettings>
{
    public void Configure(ManInBlackSettings settings)
    {
        foreach (var contribution in contributions)
            contribution.Apply(settings);
    }
}
```

- [ ] **Step 7: 实现 PipelineRegistration**

创建 `src/ManInBlack.AI/Configuration/PipelineRegistration.cs`：

```csharp
using ManInBlack.AI.Middlewares;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 一条 pipeline 注册：名称 + 构建 AgentPipelineBuilder 的委托。
/// 由 .AddPipeline 即时注册为单例，AgentFactory 构造时收集。
/// </summary>
internal sealed record PipelineRegistration(
    string Name,
    Func<AgentPipelineBuilder, AgentPipelineBuilder> Resolver);
```

- [ ] **Step 8: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests"`
Expected: PASS（4 个测试全过）。

- [ ] **Step 9: 提交**

```bash
git add src/ManInBlack.AI/Configuration/IManInBlackContribution.cs src/ManInBlack.AI/Configuration/ActionContribution.cs src/ManInBlack.AI/Configuration/SettingsMerger.cs src/ManInBlack.AI/Configuration/ManInBlackSettingsBuilder.cs src/ManInBlack.AI/Configuration/PipelineRegistration.cs src/ManInBlack.AI/ManInBlack.AI.csproj test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs
git commit -m "✨ 配置合并引擎：贡献接口、按 key 合并、IConfigureOptions 落地"
```

---

## Task 2: Builder 骨架 + Provider / ModelChoice

**Files:**
- Create: `src/ManInBlack.AI/IManInBlackBuilder.cs`
- Create: `src/ManInBlack.AI/ManInBlackBuilder.cs`
- Create: `src/ManInBlack.AI/Configuration/SubBuilders/ProviderBuilder.cs`
- Create: `src/ManInBlack.AI/Configuration/SubBuilders/ModelChoiceBuilder.cs`
- Create: `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs`（本任务只含 AddProvider/AddModelChoice）
- Test: 追加到 `test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs`

**Interfaces:**
- Consumes: `IManInBlackContribution`、`ActionContribution`（Task 1）。
- Produces:
  - `public interface IManInBlackBuilder { IServiceCollection Services { get; } }`
  - `internal sealed class ManInBlackBuilder : IManInBlackBuilder`，`internal void AddContribution(IManInBlackContribution c)`
  - `public sealed class ProviderBuilder`（`.Schema`/`.ApiKey`/`.BaseUrl`）
  - `public sealed class ModelChoiceBuilder`（`.Provider(name)`/`.ModelId`）
  - `public static class ManInBlackBuilderExtensions`：`AddProvider(this IManInBlackBuilder, string, Action<ProviderBuilder>)`、`AddProvider(this IManInBlackBuilder, string, ProviderSettings)`、`AddModelChoice(this IManInBlackBuilder, string, Action<ModelChoiceBuilder>)`、`AddModelChoice(this IManInBlackBuilder, string, ModelChoiceSettings)`。

- [ ] **Step 1: 写失败测试**

在 `ManInBlackBuilderTests.cs` 末尾追加（在类闭合 `}` 之前）：

```csharp
    [Fact]
    public void AddProvider_Delegate_WritesProvider()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx").BaseUrl("https://api.deepseek.com"));

        var settings = Merge(services);

        Assert.Equal("OpenAI", settings.Providers["default"].Schema);
        Assert.Equal("sk-xxx", settings.Providers["default"].ApiKey);
        Assert.Equal("https://api.deepseek.com", settings.Providers["default"].BaseUrl);
    }

    [Fact]
    public void AddProvider_ObjectOverload_WritesProvider()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddProvider("default", new ProviderSettings { Schema = "Anthropic", ApiKey = "k" });

        var settings = Merge(services);

        Assert.Equal("Anthropic", settings.Providers["default"].Schema);
    }

    [Fact]
    public void AddModelChoice_Delegate_WritesChoice()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddModelChoice("default", c => c.Provider("default").ModelId("gpt-4o"));

        var settings = Merge(services);

        Assert.Equal("default", settings.ModelChoices["default"].ProviderName);
        Assert.Equal("gpt-4o", settings.ModelChoices["default"].ModelId);
    }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests"`
Expected: FAIL（`ManInBlackBuilder`、`AddProvider` 等未定义）。

- [ ] **Step 3: 实现 IManInBlackBuilder + ManInBlackBuilder**

创建 `src/ManInBlack.AI/IManInBlackBuilder.cs`：

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI;

/// <summary>
/// 流式配置 builder。外部程序集（如 FeishuAdaptor）可通过 <see cref="Services"/> 挂自己的扩展方法。
/// </summary>
public interface IManInBlackBuilder
{
    IServiceCollection Services { get; }
}
```

创建 `src/ManInBlack.AI/ManInBlackBuilder.cs`：

```csharp
using ManInBlack.AI.Configuration;

namespace ManInBlack.AI;

/// <summary>
/// IManInBlackBuilder 的默认实现。同程序集内的流式扩展方法通过强转访问 internal <see cref="AddContribution"/>。
/// </summary>
internal sealed class ManInBlackBuilder(IServiceCollection services) : IManInBlackBuilder
{
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// 注册一条配置贡献（IOptions 首次 resolve 时按序合并）。
    /// </summary>
    internal void AddContribution(IManInBlackContribution contribution)
        => Services.AddSingleton<IManInBlackContribution>(contribution);
}
```

- [ ] **Step 4: 实现 ProviderBuilder + ModelChoiceBuilder**

创建 `src/ManInBlack.AI/Configuration/SubBuilders/ProviderBuilder.cs`：

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 ProviderSettings。
/// </summary>
public sealed class ProviderBuilder
{
    internal ProviderSettings Settings { get; } = new();

    public ProviderBuilder Schema(string schema) { Settings.Schema = schema; return this; }
    public ProviderBuilder ApiKey(string apiKey) { Settings.ApiKey = apiKey; return this; }
    public ProviderBuilder BaseUrl(string? baseUrl) { Settings.BaseUrl = baseUrl; return this; }
}
```

创建 `src/ManInBlack.AI/Configuration/SubBuilders/ModelChoiceBuilder.cs`：

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 ModelChoiceSettings。
/// </summary>
public sealed class ModelChoiceBuilder
{
    internal ModelChoiceSettings Settings { get; } = new();

    public ModelChoiceBuilder Provider(string providerName) { Settings.ProviderName = providerName; return this; }
    public ModelChoiceBuilder ModelId(string modelId) { Settings.ModelId = modelId; return this; }
}
```

- [ ] **Step 5: 实现 AddProvider / AddModelChoice 扩展方法**

创建 `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs`：

```csharp
using ManInBlack.AI.Configuration;

namespace ManInBlack.AI;

/// <summary>
/// IManInBlackBuilder 的流式配置扩展方法。
/// </summary>
public static class ManInBlackBuilderExtensions
{
    public static IManInBlackBuilder AddProvider(this IManInBlackBuilder builder, string name, Action<ProviderBuilder> configure)
    {
        var b = new ProviderBuilder();
        configure(b);
        return builder.AddProvider(name, b.Settings);
    }

    public static IManInBlackBuilder AddProvider(this IManInBlackBuilder builder, string name, ProviderSettings provider)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.Providers[name] = provider));
        return builder;
    }

    public static IManInBlackBuilder AddModelChoice(this IManInBlackBuilder builder, string name, Action<ModelChoiceBuilder> configure)
    {
        var b = new ModelChoiceBuilder();
        configure(b);
        return builder.AddModelChoice(name, b.Settings);
    }

    public static IManInBlackBuilder AddModelChoice(this IManInBlackBuilder builder, string name, ModelChoiceSettings choice)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.ModelChoices[name] = choice));
        return builder;
    }
}
```

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests"`
Expected: PASS（7 个测试全过）。

- [ ] **Step 7: 提交**

```bash
git add src/ManInBlack.AI/IManInBlackBuilder.cs src/ManInBlack.AI/ManInBlackBuilder.cs src/ManInBlack.AI/ManInBlackBuilderExtensions.cs src/ManInBlack.AI/Configuration/SubBuilders/ test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs
git commit -m "✨ Builder 骨架与 Provider/ModelChoice 流式扩展"
```

---

## Task 3: Agent + Pipeline（AgentFactory ctor 收集 PipelineRegistration）

**Files:**
- Create: `src/ManInBlack.AI/Configuration/SubBuilders/AgentBuilder.cs`
- Modify: `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs`（追加 AddAgent / AddPipeline）
- Modify: `src/ManInBlack.AI/AgentFactory.cs`（ctor 增加 `IEnumerable<PipelineRegistration>`）
- Modify: `test/ManInBlack.AI.Tests/AgentFactoryTests.cs`（`CreateFactory` 适配）
- Test: 追加到 `test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs`

**Interfaces:**
- Consumes: `ManInBlackBuilder.AddContribution`、`AgentDefinition`（Abstraction）、`AgentPipelineBuilder`。
- Produces:
  - `public sealed class AgentBuilder`（`.Description`/`.Instruction`/`.Pipeline`/`.SubAgents`/`.ModelChoice`，末尾隐式产出 `AgentDefinition`）
  - `AddAgent(this IManInBlackBuilder, string, Action<AgentBuilder>)`、`AddAgent(this IManInBlackBuilder, AgentDefinition)`
  - `AddPipeline(this IManInBlackBuilder, string, Func<AgentPipelineBuilder, AgentPipelineBuilder>)`
  - `AgentFactory` ctor 签名变为 `(IServiceScopeFactory, ILogger<AgentFactory>, IEnumerable<AgentDefinition>, IEnumerable<PipelineRegistration>)`。

- [ ] **Step 1: 写失败测试（builder 侧）**

在 `ManInBlackBuilderTests.cs` 追加：

```csharp
    [Fact]
    public void AddAgent_Delegate_RegistersDefinitionAndWritesSettings()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddAgent("console-agent", a => a
            .Instruction("你是AI助手")
            .Pipeline("default")
            .SubAgents("sub"));

        var settings = Merge(services);

        // settings.Agents 供校验
        Assert.Equal("你是AI助手", settings.Agents["console-agent"].Instruction);
        Assert.Contains("sub", settings.Agents["console-agent"].SubAgents);
        // AgentDefinition 即时注册为单例
        var defs = services.BuildServiceProvider().GetServices<AgentDefinition>();
        Assert.Single(defs, d => d.Name == "console-agent" && d.PipelineName == "default");
    }

    [Fact]
    public void AddPipeline_RegistersPipelineRegistrationSingleton()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddPipeline("custom", b => b.UseSimple());

        var regs = services.BuildServiceProvider().GetServices<PipelineRegistration>();
        Assert.Single(regs, r => r.Name == "custom");
    }
```

> 注意：测试引用了 `AgentDefinition`，需在文件顶部 `using` 区确认已有 `using ManInBlack.AI.Abstraction;`（已有则跳过）。

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests"`
Expected: FAIL（`AgentBuilder`、`AddAgent`、`AddPipeline` 未定义）。

- [ ] **Step 3: 实现 AgentBuilder**

创建 `src/ManInBlack.AI/Configuration/SubBuilders/AgentBuilder.cs`：

```csharp
using ManInBlack.AI.Abstraction;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 AgentDefinition。
/// </summary>
public sealed class AgentBuilder
{
    private readonly AgentDefinition _definition = new();

    internal AgentBuilder(string name) => _definition.Name = name;

    public AgentBuilder Description(string description) { _definition.Description = description; return this; }
    public AgentBuilder Instruction(string instruction) { _definition.Instruction = instruction; return this; }
    public AgentBuilder Pipeline(string pipelineName) { _definition.PipelineName = pipelineName; return this; }
    public AgentBuilder SubAgents(params string[] subAgents) { _definition.SubAgents = [..subAgents]; return this; }
    public AgentBuilder ModelChoice(string? modelChoiceName) { _definition.ModelChoiceName = modelChoiceName; return this; }

    internal AgentDefinition Build() => _definition;
}
```

- [ ] **Step 4: 追加 AddAgent / AddPipeline 扩展**

在 `ManInBlackBuilderExtensions.cs` 的类内（`AddModelChoice` 之后、闭合 `}` 之前）追加：

```csharp
    public static IManInBlackBuilder AddAgent(this IManInBlackBuilder builder, string name, Action<AgentBuilder> configure)
    {
        var a = new AgentBuilder(name);
        configure(a);
        return builder.AddAgent(a.Build());
    }

    public static IManInBlackBuilder AddAgent(this IManInBlackBuilder builder, AgentDefinition definition)
    {
        var concrete = (ManInBlackBuilder)builder;
        // A：即时注册 AgentDefinition 单例（AgentFactory ctor 收集）
        concrete.Services.AddSingleton(definition);
        // B：贡献写入 settings.Agents，供 ValidateManInBlackSettings 校验
        concrete.AddContribution(new ActionContribution(s =>
        {
            s.Agents[definition.Name] = new AgentSettings
            {
                Description = definition.Description,
                Instruction = definition.Instruction,
                PipelineName = definition.PipelineName,
                SubAgents = definition.SubAgents,
                ModelChoiceName = definition.ModelChoiceName,
            };
        }));
        return builder;
    }

    public static IManInBlackBuilder AddPipeline(this IManInBlackBuilder builder, string name, Func<AgentPipelineBuilder, AgentPipelineBuilder> resolver)
    {
        // 即时注册 PipelineRegistration 单例（AgentFactory ctor 收集）
        ((ManInBlackBuilder)builder).Services.AddSingleton(new PipelineRegistration(name, resolver));
        return builder;
    }
```

在 `ManInBlackBuilderExtensions.cs` 顶部 using 区确认/添加：

```csharp
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Middlewares;
```

- [ ] **Step 5: 修改 AgentFactory ctor**

修改 `src/ManInBlack.AI/AgentFactory.cs`。把 ctor 与紧随其后注册内置 pipeline 的部分改为：

```csharp
    public AgentFactory(
        IServiceScopeFactory scopeFactory,
        ILogger<AgentFactory> logger,
        IEnumerable<AgentDefinition> definitions,
        IEnumerable<PipelineRegistration> pipelines)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        foreach (var def in definitions)
            RegisterDefinition(def);

        // 内置管道预设
        _pipelineResolvers["default"] = builder => builder.UseDefault();
        _pipelineResolvers["simple"] = builder => builder.UseSimple();

        // 收集 builder 期（.AddPipeline）注册的 pipeline，覆盖同名内置
        foreach (var reg in pipelines)
            _pipelineResolvers[reg.Name] = reg.Resolver;
    }
```

在文件顶部 using 区添加：

```csharp
using ManInBlack.AI.Configuration;
```

- [ ] **Step 6: 适配 AgentFactoryTests.CreateFactory**

修改 `test/ManInBlack.AI.Tests/AgentFactoryTests.cs` 中 `CreateFactory`，补上新参数 `[]`：

```csharp
    private static AgentFactory CreateFactory()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        return new AgentFactory(scopeFactory, NullLogger<AgentFactory>.Instance, [], []);
    }
```

- [ ] **Step 7: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests|FullyQualifiedName~AgentFactoryTests"`
Expected: PASS（builder 与 AgentFactory 测试全过）。

- [ ] **Step 8: 全量构建确认无回归**

Run: `dotnet build src/ManInBlack.AI`
Expected: 成功（确认 DependencyInjection 中 `AddSingleton<AgentFactory>()` 的 DI 注册仍能解析新 ctor——DI 自动注入 `IEnumerable<PipelineRegistration>`，无注册时为空）。

- [ ] **Step 9: 提交**

```bash
git add src/ManInBlack.AI/Configuration/SubBuilders/AgentBuilder.cs src/ManInBlack.AI/ManInBlackBuilderExtensions.cs src/ManInBlack.AI/AgentFactory.cs test/ManInBlack.AI.Tests/AgentFactoryTests.cs test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs
git commit -m "✨ AddAgent/AddPipeline 流式扩展；AgentFactory 收集 builder 期 pipeline"
```

---

## Task 4: Hook / McpServer / Storage / Sandbox 流式扩展

**Files:**
- Create: `src/ManInBlack.AI/Configuration/SubBuilders/HookBuilder.cs`
- Create: `src/ManInBlack.AI/Configuration/SubBuilders/McpServerBuilder.cs`
- Create: `src/ManInBlack.AI/Configuration/SubBuilders/StorageBuilder.cs`
- Modify: `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs`（追加 AddHook/AddMcpServer/UseStorage/UseSandbox）
- Test: 追加到 `test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs`

**Interfaces:**
- Consumes: `HookSettings`、`McpServerSettings`、`StorageSettings`/`WorkspaceSettings`/`WorkspaceMode`（Abstraction）。
- Produces: `HookBuilder`、`McpServerBuilder`、`StorageBuilder`；扩展 `AddHook`/`AddMcpServer`/`UseStorage`/`UseSandbox`。

- [ ] **Step 1: 写失败测试**

在 `ManInBlackBuilderTests.cs` 追加（顶部 using 区按需补 `using ManInBlack.AI.Abstraction.Storage;`）：

```csharp
    [Fact]
    public void AddHook_Accumulates()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddHook(h => h.On("before_run").Run("echo a"));
        builder.AddHook(h => h.On("after_run").Run("echo b"));

        var settings = Merge(services);

        Assert.Equal(2, settings.Hooks.Count);
        Assert.Equal("before_run", settings.Hooks[0].Event);
        Assert.Equal("echo b", settings.Hooks[1].Script);
    }

    [Fact]
    public void AddMcpServer_Delegate_WritesServer()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.AddMcpServer("tavily", m => m.Endpoint("https://mcp.tavily.com/mcp").Header("Authorization", "Bearer xxx"));

        var settings = Merge(services);

        Assert.Equal("https://mcp.tavily.com/mcp", settings.McpServers["tavily"].Endpoint);
        Assert.Equal("Bearer xxx", settings.McpServers["tavily"].Headers!["Authorization"]);
    }

    [Fact]
    public void UseStorage_Delegate_WritesStorage()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.UseStorage(s => s.RootPath("/data/mib").Workspace(w => w.Mode(WorkspaceMode.CustomPath).CustomPath("/ws")));

        var settings = Merge(services);

        Assert.Equal("/data/mib", settings.Storage!.RootPath);
        Assert.Equal(WorkspaceMode.CustomPath, settings.Storage!.Workspace!.Mode);
        Assert.Equal("/ws", settings.Storage!.Workspace!.CustomPath);
    }

    [Fact]
    public void UseSandbox_SetsFlag()
    {
        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);

        builder.UseSandbox();

        var settings = Merge(services);

        Assert.True(settings.UseSandbox);
    }
```

> 说明：`HookSettings` 的字段名以现有定义为准——若实际为 `Event`/`Script` 之外的名字（如 `HookEvent`/`ScriptPath`），实现 HookBuilder 时对齐真实字段；测试中的属性访问须与 `HookSettings` 真实属性一致。实现前先 `dotnet build` 报错定位真实字段名。

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests"`
Expected: FAIL（`HookBuilder`、`McpServerBuilder`、`StorageBuilder`、扩展方法未定义）。

- [ ] **Step 3: 先确认 HookSettings 真实字段**

Run: `grep -rn "class HookSettings" src/ManInBlack.AI/Configuration/` 然后读该文件，记下真实属性名（如 `Event`/`HookEvent`、`Script`/`ScriptPath`/`Path`）。后续 HookBuilder 与测试断言都用真实字段名。

> 若 `HookSettings` 字段与测试 Step 1 不符，回头把测试断言（`settings.Hooks[0].Event` 等）改成真实字段名，保持一致。

- [ ] **Step 4: 实现 HookBuilder**

创建 `src/ManInBlack.AI/Configuration/SubBuilders/HookBuilder.cs`（字段名以 Step 3 确认的真实属性为准；下面以 `Event`/`Script` 为例）：

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 HookSettings。
/// </summary>
public sealed class HookBuilder
{
    internal HookSettings Settings { get; } = new();

    public HookBuilder On(string hookEvent) { Settings.Event = hookEvent; return this; }
    public HookBuilder Run(string script) { Settings.Script = script; return this; }
}
```

- [ ] **Step 5: 实现 McpServerBuilder**

创建 `src/ManInBlack.AI/Configuration/SubBuilders/McpServerBuilder.cs`：

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 McpServerSettings。
/// </summary>
public sealed class McpServerBuilder
{
    internal McpServerSettings Settings { get; } = new();

    public McpServerBuilder Transport(string transport) { Settings.Transport = transport; return this; }
    public McpServerBuilder Command(string command) { Settings.Command = command; return this; }
    public McpServerBuilder Arguments(params string[] args) { Settings.Arguments = [..args]; return this; }
    public McpServerBuilder WorkingDirectory(string dir) { Settings.WorkingDirectory = dir; return this; }
    public McpServerBuilder Environment(string key, string? value)
    {
        Settings.Environment ??= new Dictionary<string, string?>();
        Settings.Environment[key] = value;
        return this;
    }
    public McpServerBuilder Endpoint(string endpoint) { Settings.Endpoint = endpoint; return this; }
    public McpServerBuilder Header(string key, string value)
    {
        Settings.Headers ??= new Dictionary<string, string>();
        Settings.Headers[key] = value;
        return this;
    }
    public McpServerBuilder Enabled(bool enabled) { Settings.Enabled = enabled; return this; }
}
```

- [ ] **Step 6: 实现 StorageBuilder**

先确认 `WorkspaceSettings` 的真实属性（`Mode`、`CustomPath` 等）。创建 `src/ManInBlack.AI/Configuration/SubBuilders/StorageBuilder.cs`：

```csharp
using ManInBlack.AI.Abstraction.Storage;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 StorageSettings。
/// </summary>
public sealed class StorageBuilder
{
    internal StorageSettings Settings { get; } = new();

    public StorageBuilder RootPath(string rootPath) { Settings.RootPath = rootPath; return this; }
    public StorageBuilder Workspace(Action<WorkspaceBuilder> configure)
    {
        var b = new WorkspaceBuilder();
        configure(b);
        Settings.Workspace = b.Settings;
        return this;
    }
}

public sealed class WorkspaceBuilder
{
    internal WorkspaceSettings Settings { get; } = new();

    public WorkspaceBuilder Mode(WorkspaceMode mode) { Settings.Mode = mode; return this; }
    public WorkspaceBuilder CustomPath(string path) { Settings.CustomPath = path; return this; }
}
```

> 若 `WorkspaceSettings` 的属性名/类型与上面不符（例如 `CustomPath` 实为别的名），以真实定义为准调整。

- [ ] **Step 7: 追加 AddHook / AddMcpServer / UseStorage / UseSandbox 扩展**

在 `ManInBlackBuilderExtensions.cs` 类内追加：

```csharp
    public static IManInBlackBuilder AddHook(this IManInBlackBuilder builder, Action<HookBuilder> configure)
    {
        var h = new HookBuilder();
        configure(h);
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.Hooks.Add(h.Settings)));
        return builder;
    }

    public static IManInBlackBuilder AddMcpServer(this IManInBlackBuilder builder, string name, Action<McpServerBuilder> configure)
    {
        var m = new McpServerBuilder();
        configure(m);
        return builder.AddMcpServer(name, m.Settings);
    }

    public static IManInBlackBuilder AddMcpServer(this IManInBlackBuilder builder, string name, McpServerSettings server)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.McpServers[name] = server));
        return builder;
    }

    public static IManInBlackBuilder UseStorage(this IManInBlackBuilder builder, Action<StorageBuilder> configure)
    {
        var s = new StorageBuilder();
        configure(s);
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(settings => settings.Storage = s.Settings));
        return builder;
    }

    public static IManInBlackBuilder UseSandbox(this IManInBlackBuilder builder)
    {
        ((ManInBlackBuilder)builder).AddContribution(new ActionContribution(s => s.UseSandbox = true));
        return builder;
    }
```

- [ ] **Step 8: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests"`
Expected: PASS（全部 builder 测试）。

- [ ] **Step 9: 提交**

```bash
git add src/ManInBlack.AI/Configuration/SubBuilders/HookBuilder.cs src/ManInBlack.AI/Configuration/SubBuilders/McpServerBuilder.cs src/ManInBlack.AI/Configuration/SubBuilders/StorageBuilder.cs src/ManInBlack.AI/ManInBlackBuilderExtensions.cs test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs
git commit -m "✨ Hook/McpServer/Storage/Sandbox 流式扩展"
```

---

## Task 5: 源贡献 UseJson / UseConfiguration

**Files:**
- Modify: `src/ManInBlack.AI/Configuration/ManInBlackConfigurationBuilder.cs`（新增 `LoadSettings()`）
- Modify: `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs`（追加 UseJson / UseConfiguration）
- Test: 追加到 `test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs`

**Interfaces:**
- Consumes: `SettingsMerger.Merge`（Task 1）、`ManInBlackConfigurationBuilder.EnsureSettingsFile`。
- Produces:
  - `ManInBlackConfigurationBuilder.LoadSettings()` → 读 `~/.man-in-black/settings.json`（缺失则建默认）并绑定到新 `ManInBlackSettings`。
  - `UseJson(this IManInBlackBuilder)`：读文件 → 即时注册每个 agent 的 AgentDefinition 单例 + 贡献合并文件内容。
  - `UseConfiguration(this IManInBlackBuilder, IConfiguration)`：绑定到 settings + 贡献合并 + 单独绑定 FeishuSettings（兼容适配器读取）。

- [ ] **Step 1: 写失败测试（UseConfiguration，避免触碰真实文件系统）**

在 `ManInBlackBuilderTests.cs` 顶部 using 区加：

```csharp
using Microsoft.Extensions.Configuration;
```

追加测试：

```csharp
    [Fact]
    public void UseConfiguration_BindsAndMergesAndBindsFeishu()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "from-cfg",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
            ["Agents:console-agent:Instruction"] = "cfg agent",
            ["Agents:console-agent:PipelineName"] = "default",
            ["Feishu:AppId"] = "cli_xxx",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);
        builder.UseConfiguration(configuration);

        var settings = Merge(services);

        Assert.Equal("from-cfg", settings.Providers["default"].ApiKey);
        Assert.Equal("cfg agent", settings.Agents["console-agent"].Instruction);
        // Feishu 单独绑定
        var feishu = services.BuildServiceProvider().GetRequiredService<IOptions<FeishuSettings>>().Value;
        Assert.Equal("cli_xxx", feishu.AppId);
        // 每个 agent 即时注册 AgentDefinition 单例
        Assert.Single(services.BuildServiceProvider().GetServices<AgentDefinition>(), d => d.Name == "console-agent");
    }

    [Fact]
    public void UseJson_ThenAddProvider_DelegateOverridesJsonByKey()
    {
        // 构造一个临时 settings.json 路径需要触及用户目录；改为直接验证 UseJson 内部用的是 LoadSettings+SettingsMerger：
        // 这里用 UseConfiguration 模拟 JSON 源，再追加委托覆盖，验证 last-write-wins。
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "from-json",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        var builder = new ManInBlackBuilder(services);
        builder.UseConfiguration(configuration);       // 模拟 JSON 源（链首）
        builder.AddProvider("default", p => p.Schema("OpenAI").ApiKey("from-delegate")); // 覆盖

        var settings = Merge(services);

        Assert.Equal("from-delegate", settings.Providers["default"].ApiKey);
    }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests"`
Expected: FAIL（`UseConfiguration` 未定义）。

- [ ] **Step 3: 给 ManInBlackConfigurationBuilder 加 LoadSettings**

修改 `src/ManInBlack.AI/Configuration/ManInBlackConfigurationBuilder.cs`，在类内（`AddManInBlackSettings` 方法之后）追加：

```csharp
    /// <summary>
    /// 读取 ~/.man-in-black/settings.json（缺失则创建默认）并绑定到 ManInBlackSettings。
    /// 供 .UseJson() 复用。
    /// </summary>
    public static ManInBlackSettings LoadSettings()
    {
        EnsureSettingsFile();
        var settings = new ManInBlackSettings();
        BuildConfiguration().Bind(settings);
        return settings;
    }
```

- [ ] **Step 4: 实现 UseJson / UseConfiguration 扩展**

在 `ManInBlackBuilderExtensions.cs` 顶部 using 区加：

```csharp
using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.Configuration;
```

类内追加：

```csharp
    /// <summary>
    /// 载入 ~/.man-in-black/settings.json 作为配置源（缺失则创建默认）。
    /// 位置决定合并层：放链首则后续委托覆盖 JSON 同名 key。
    /// </summary>
    public static IManInBlackBuilder UseJson(this IManInBlackBuilder builder)
    {
        var loaded = ManInBlackConfigurationBuilder.LoadSettings();
        return ApplySource(builder, loaded);
    }

    /// <summary>
    /// 复用已有 IConfiguration（Web 场景）作为配置源。同时绑定 FeishuSettings 供适配器读取。
    /// </summary>
    public static IManInBlackBuilder UseConfiguration(this IManInBlackBuilder builder, IConfiguration configuration)
    {
        var loaded = new ManInBlackSettings();
        configuration.Bind(loaded);
        builder.Services.Configure<FeishuSettings>(configuration.GetSection("Feishu"));
        return ApplySource(builder, loaded);
    }

    private static IManInBlackBuilder ApplySource(IManInBlackBuilder builder, ManInBlackSettings source)
    {
        var concrete = (ManInBlackBuilder)builder;
        // A：即时注册每个 agent 的 AgentDefinition 单例
        foreach (var (name, agent) in source.Agents)
        {
            concrete.Services.AddSingleton(new AgentDefinition
            {
                Name = name,
                Description = agent.Description,
                Instruction = agent.Instruction,
                PipelineName = agent.PipelineName,
                SubAgents = agent.SubAgents,
                ModelChoiceName = agent.ModelChoiceName,
            });
        }
        // B：贡献合并文件/IConfiguration 内容进 settings
        concrete.AddContribution(new ActionContribution(s => SettingsMerger.Merge(s, source)));
        return builder;
    }
```

> `builder.Services.Configure<FeishuSettings>(...)` 需要 `Microsoft.Extensions.DependencyInjection`（已在 using）与 `Microsoft.Extensions.Configuration`（已加）。`Configure<T>(IConfiguration)` 会自动 `AddOptions()`。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ManInBlackBuilderTests"`
Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/ManInBlack.AI/Configuration/ManInBlackConfigurationBuilder.cs src/ManInBlack.AI/ManInBlackBuilderExtensions.cs test/ManInBlack.AI.Tests/Configuration/ManInBlackBuilderTests.cs
git commit -m "✨ UseJson/UseConfiguration 源贡献，按 key 与委托合并"
```

---

## Task 6: 新 DI 入口 AddManInBlack() + 旧入口改写为薄封装

**Files:**
- Modify: `src/ManInBlack.AI/DependencyInjection.cs`
- Test: 新建 `test/ManInBlack.AI.Tests/Configuration/AddManInBlackEndToEndTests.cs`

**Interfaces:**
- Consumes: `ManInBlackBuilder`、`ManInBlackSettingsBuilder`、`ValidateManInBlackSettings`、`SettingsLoader.GetDefaultModelChoice`、全部流式扩展。
- Produces：
  - 新重载 `IServiceCollection AddManInBlack()`（无参，返回 builder 之前先注册核心服务与合并基础设施）。
  - `AddManInBlackFromSettings` / `AddManInBlackFromConfiguration` 改写为调用 `AddManInBlack().UseJson()` / `.UseConfiguration(cfg)`。
  - `AddManInBlack(Action<ManInBlackOptions>)` 标 `[Obsolete]`，内部映射 ModelChoice/Storage/UseSandbox 到 builder。

- [ ] **Step 1: 写端到端失败测试**

创建 `test/ManInBlack.AI.Tests/Configuration/AddManInBlackEndToEndTests.cs`：

```csharp
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class AddManInBlackEndToEndTests
{
    [Fact]
    public void AddManInBlack_PureDelegate_ResolvesSettingsAndDefinition()
    {
        var services = new ServiceCollection();
        services.AddManInBlack()
            .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx"))
            .AddModelChoice("default", c => c.Provider("default").ModelId("gpt-4o"))
            .AddAgent("a1", a => a.Instruction("hi").Pipeline("simple"));

        var sp = services.BuildServiceProvider();

        var settings = sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value;
        Assert.Equal("sk-xxx", settings.Providers["default"].ApiKey);

        var factory = sp.GetRequiredService<AgentFactory>();
        Assert.Equal("a1", factory.GetDefinition("a1").Name);
    }

    [Fact]
    public void AddManInBlack_DelegateOverriddenFromJsonPath_WorksViaUseConfiguration()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "from-json",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
        };
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        services.AddManInBlack()
            .UseConfiguration(cfg)
            .AddProvider("default", p => p.Schema("OpenAI").ApiKey("from-delegate"));

        var settings = services.BuildServiceProvider().GetRequiredService<IOptions<ManInBlackSettings>>().Value;
        Assert.Equal("from-delegate", settings.Providers["default"].ApiKey);
    }

    [Fact]
    public void AddManInBlack_ValidationFails_WhenNoProvider()
    {
        var services = new ServiceCollection();
        services.AddManInBlack().UseSandbox();

        var sp = services.BuildServiceProvider();
        // 解析 IOptions<ManInBlackSettings>.Value 触发校验
        var ex = Assert.Throws<OptionsValidationException>(() =>
            sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value);
        Assert.Contains("Providers", ex.Message);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AddManInBlackEndToEndTests"`
Expected: FAIL（`AddManInBlack()` 无参重载不存在 / 校验未注册）。

- [ ] **Step 3: 新增 AgentStorageOptionsConfigurer（从合并后的 settings 映射存储配置）**

`services.Configure<AgentStorageOptions>(Action<T>)` 的回调在 resolve 期无法拿到 `IServiceProvider`，因此读 `IOptions<ManInBlackSettings>` 必须用 `IConfigureOptions<AgentStorageOptions>`。

创建 `src/ManInBlack.AI/Configuration/AgentStorageOptionsConfigurer.cs`：

```csharp
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 从合并后的 ManInBlackSettings.Storage 映射到运行期 AgentStorageOptions。
/// </summary>
internal sealed class AgentStorageOptionsConfigurer(IOptions<ManInBlackSettings> settings)
    : IConfigureOptions<AgentStorageOptions>
{
    public void Configure(AgentStorageOptions options)
    {
        var storage = settings.Value.Storage;
        if (storage.RootPath is not null)
            options.RootPath = storage.RootPath;
        if (storage.Workspace is not null)
            options.Workspace = storage.Workspace;
    }
}
```

> 类型核对：`ManInBlackSettings.Storage` 是 `StorageSettings`（`RootPath: string?`、`Workspace: WorkspaceSettings?`）；`AgentStorageOptions` 的 `RootPath`/`Workspace` 与之兼容（原 `AddManInBlackFromConfiguration` 即如此赋值）。`WorkspaceSettings` 与 `WorkspaceMode` 在 `ManInBlack.AI.Abstraction.Storage`。

- [ ] **Step 4: 重写 DependencyInjection.cs（新增无参 AddManInBlack()）**

打开 `src/ManInBlack.AI/DependencyInjection.cs`。保留 `ManInBlackOptions` 类、`AddAgentDefinition`。把 `extension(IServiceCollection services)` 块内的原 `AddManInBlack(Action<ManInBlackOptions>)`、`AddManInBlackFromSettings`、`AddManInBlackFromConfiguration` 三个方法整段替换为下面的无参 `AddManInBlack()`（旧入口在 Step 5 以薄封装形式重新加回）：

```csharp
        /// <summary>
        /// 注册 ManInBlack 全部核心服务，返回流式 builder。
        /// 默认不读取任何文件；需 JSON 时链式调用 .UseJson()，需复用 IConfiguration 时调用 .UseConfiguration(cfg)。
        /// </summary>
        public IManInBlackBuilder AddManInBlack()
        {
            // 合并基础设施：贡献 → IConfigureOptions<ManInBlackSettings>，再走现有校验器
            services.AddOptions();
            services.AddSingleton<IConfigureOptions<ManInBlackSettings>>(
                sp => new ManInBlackSettingsBuilder(sp.GetServices<IManInBlackContribution>()));
            services.AddSingleton<IValidateOptions<ManInBlackSettings>, ValidateManInBlackSettings>();

            // AgentStorageOptions：从合并后的 settings.Storage 映射
            services.AddSingleton<IConfigureOptions<AgentStorageOptions>, AgentStorageOptionsConfigurer>();

            // 默认 ModelChoice 单例：从合并后的 settings 解析
            services.AddSingleton<ModelChoice>(sp =>
                sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value.GetDefaultModelChoice());

            services.AddScoped<AgentPipelineBuilder>();
            services.AddScoped<AgentContext>();
            services.AddSingleton<AgentFactory>();

            services.AddHttpClient(string.Empty)
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });

            services.AddScoped<IChatClient>(sp =>
            {
                var choice = sp.GetRequiredService<ModelChoice>();
                return ChatClientProviderExtensions.CreateChatClient(
                    sp.GetRequiredService<IHttpClientFactory>(), choice);
            });

            services.TryAddSingleton<IAgentStateStorage>(
                sp => (IAgentStateStorage)sp.GetRequiredService<ISessionStorage>());
            services.TryAddSingleton<ICheckpointPolicy, AfterToolCallPolicy>();

            services.AddScoped<IUserWorkspace>(sp =>
            {
                var ws = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.Workspace;
                return ws.Mode switch
                {
                    WorkspaceMode.CurrentDirectory => new CurrentDirectoryWorkspace(),
                    WorkspaceMode.CustomPath => new CustomPathWorkspace(
                        sp.GetRequiredService<IOptions<AgentStorageOptions>>()),
                    _ => new FileUserWorkspace(
                        sp.GetRequiredService<IOptions<AgentStorageOptions>>(),
                        sp.GetRequiredService<AgentContext>(),
                        sp.GetRequiredService<IUserStorage>())
                };
            });

            services.AddAutoRegisteredServices();

            // 沙盒：UseSandbox 在 IOptions resolve 时才确定，故做成 resolve 期工厂
            services.AddScoped<IShellExecutor>(sp =>
            {
                var useSandbox = sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value.UseSandbox;
                if (OperatingSystem.IsLinux() && useSandbox)
                    return new BwarpShellExecutor();
                return new ProcessShellExecutor();
            });
            services.AddToolHandlers();

            // MCP
            services.AddSingleton<McpClientHostedService>();
            services.AddSingleton<IMcpToolProvider, McpToolProvider>();
            services.AddHostedService(sp => sp.GetRequiredService<McpClientHostedService>());

            return new ManInBlackBuilder(services);
        }
```

> `AddOptions()` 必须显式调用：新流程不再走 `Configure<ManInBlackSettings>(configuration)`（那个内部会自动 AddOptions），而改为直接注册 `IConfigureOptions`，故需手动启用 Options 基础设施。

- [ ] **Step 5: 改写旧三入口为薄封装**

在 `extension` 块内、`AddManInBlack()` 之后追加：

```csharp
        /// <summary>
        /// 从 ~/.man-in-black/settings.json 加载配置并注册所有服务（旧入口，等价于 AddManInBlack().UseJson()）。
        /// </summary>
        public IServiceCollection AddManInBlackFromSettings(Action<ManInBlackOptions>? configure = null)
        {
            var builder = services.AddManInBlack().UseJson();
            ApplyLegacyOptions(builder, configure);
            return services;
        }

        /// <summary>
        /// 从给定 IConfiguration 加载配置并注册所有服务（旧入口，等价于 AddManInBlack().UseConfiguration(cfg)）。
        /// </summary>
        public IServiceCollection AddManInBlackFromConfiguration(
            IConfiguration configuration,
            Action<ManInBlackOptions>? configure = null)
        {
            var builder = services.AddManInBlack().UseConfiguration(configuration);
            ApplyLegacyOptions(builder, configure);
            return services;
        }

        /// <summary>
        /// [Obsolete] 旧的窄委托入口。改用 services.AddManInBlack().AddProvider/... 流式 API。
        /// </summary>
        [Obsolete("改用 services.AddManInBlack().AddProvider(...).AddModelChoice(...) 流式 API")]
        public IServiceCollection AddManInBlack(Action<ManInBlackOptions> configure)
        {
            // 复用与 FromSettings/FromConfiguration 相同的旧选项映射逻辑（见 ApplyLegacyOptions），避免重复
            ApplyLegacyOptions(services.AddManInBlack(), configure);
            return services;
        }
```

> `ApplyLegacyOptions` 用于把旧入口的 `Action<ManInBlackOptions>` 透传委托映射上去（多数场景为 null）。在 `extension` 块**之外**（`DependencyInjection` 静态类内、`extension` 块之后）加一个私有静态辅助：

在文件底部、`DependencyInjection` 类闭合 `}` 之前（`extension` 块之后）加：

```csharp
    private static void ApplyLegacyOptions(IManInBlackBuilder builder, Action<ManInBlackOptions>? configure)
    {
        if (configure is null) return;
        var options = new ManInBlackOptions();
        configure(options);
        builder.AddProvider("default", p => p
            .Schema(options.ModelChoice.Schema)
            .ApiKey(options.ModelChoice.ApiKey)
            .BaseUrl(string.IsNullOrEmpty(options.ModelChoice.BaseUrl) ? null : options.ModelChoice.BaseUrl));
        builder.AddModelChoice("default", c => c.Provider("default").ModelId(options.ModelChoice.ModelId));
        if (options.Storage.RootPath is not null || options.Storage.Workspace is not null)
            builder.UseStorage(s =>
            {
                if (options.Storage.RootPath is not null) s.RootPath(options.Storage.RootPath);
                if (options.Storage.Workspace is not null) s.Workspace(w => w.Mode(options.Storage.Workspace.Mode).CustomPath(options.Storage.Workspace.CustomPath));
            });
        if (options.UseSandbox)
            builder.UseSandbox();
    }
```

- [ ] **Step 6: 清理旧 `AddManInBlackFromConfiguration` 里残留的 settings.Agents→AgentDefinition 转换**

旧 `AddManInBlackFromConfiguration` 内有一段手动 `foreach settings.Agents` 注册 AgentDefinition 的循环——现在 `.UseConfiguration()` 已在 `ApplySource` 里即时注册，重复注册会触发 `AgentFactory.RegisterDefinition` 的同名抛错。**删除**旧 `AddManInBlackFromConfiguration` 实现里那段 `foreach (var (agentName, agentSettings) in settings.Agents) { services.AddSingleton(new AgentDefinition {...}); }`（因为整个方法已被 Step 5 的薄封装替换，确认旧实现整段被替换、无残留）。

- [ ] **Step 7: 确认 using 区齐全**

`DependencyInjection.cs` 顶部需包含（多数已有）：

```csharp
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.Options;
```

`AgentStorageOptionsConfigurer.cs`、`IConfigureOptions` 等需要 `Microsoft.Extensions.Options`。

- [ ] **Step 8: 构建并运行端到端测试**

Run: `dotnet build src/ManInBlack.AI`
Expected: 成功。

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~AddManInBlackEndToEndTests"`
Expected: PASS（3 个测试）。

- [ ] **Step 9: 全量回归测试**

Run: `dotnet test test/ManInBlack.AI.Tests`
Expected: 全部 PASS（旧入口仍工作）。

- [ ] **Step 10: 提交**

```bash
git add src/ManInBlack.AI/DependencyInjection.cs src/ManInBlack.AI/Configuration/AgentStorageOptionsConfigurer.cs test/ManInBlack.AI.Tests/Configuration/AddManInBlackEndToEndTests.cs
git commit -m "✨ 新 DI 入口 AddManInBlack() 流式 builder；旧入口改写为薄封装"
```

---

## Task 7: FeishuAdaptor AddFeishu 扩展

**Files:**
- Create: `demo/FeishuAdaptor/FeishuBuilderExtensions.cs`
- Test: 确认 `FeishuSettings` 类型路径；如 `FeishuAdaptor.Tests` 存在则加测试，否则手动验证

**Interfaces:**
- Consumes: `IManInBlackBuilder.Services`、`FeishuSettings`（核心库 POCO）。
- Produces: `FeishuBuilderExtensions.AddFeishu(this IManInBlackBuilder, Action<FeishuSettings>)`。

- [ ] **Step 1: 实现扩展**

创建 `demo/FeishuAdaptor/FeishuBuilderExtensions.cs`：

```csharp
using ManInBlack.AI;
using ManInBlack.AI.Configuration;

namespace FeishuAdaptor;

/// <summary>
/// 在核心 builder 之上挂飞书配置（核心库不感知适配器概念）。
/// </summary>
public static class FeishuBuilderExtensions
{
    public static IManInBlackBuilder AddFeishu(this IManInBlackBuilder builder, Action<FeishuSettings> configure)
    {
        builder.Services.Configure(configure);
        return builder;
    }
}
```

- [ ] **Step 2: 确认命名空间与 FeishuSettings 可见性**

Run: `dotnet build demo/FeishuAdaptor`
Expected: 成功（`FeishuSettings` 在 `ManInBlack.AI.Configuration`，public；`IManInBlackBuilder` 在 `ManInBlack.AI`，public）。

- [ ] **Step 3: 提交**

```bash
git add demo/FeishuAdaptor/FeishuBuilderExtensions.cs
git commit -m "✨ FeishuAdaptor 提供 AddFeishu 扩展方法"
```

> 单测：若 `test/FeishuAdaptor.Tests` 存在且引用了 `ManInBlack.AI`，可加一个 `[Fact]`：`new ManInBlackBuilder(new ServiceCollection()).AddFeishu(f => f.AppId = "x")` 后 `IOptions<FeishuSettings>.Value.AppId == "x"`。若不存在，跳过（Task 8 迁移时会端到端验证）。

---

## Task 8: demo 迁移到新 API

**Files:**
- Modify: `demo/AgentConsole/Program.cs`
- Modify: `demo/FeishuAdaptor/Program.cs`
- Modify: `demo/GitHubAdaptor/Program.cs`

**Interfaces:** 消费 Task 6/7 的公开 API。

- [ ] **Step 1: 迁移 AgentConsole**

修改 `demo/AgentConsole/Program.cs` 开头 DI 部分。把：

```csharp
var services = new ServiceCollection();
services.AddManInBlackFromSettings();
```

与随后的 `factory.RegisterPipeline("sub-agent", ...)`，改为：

```csharp
var services = new ServiceCollection();
services.AddManInBlack()
    .UseJson()
    .AddPipeline("sub-agent", builder => builder
        .Use<EventPublishingMiddleware>()
        .Use<ToolsMiddleware>()
        .UseSimple());

var rootSp = services.BuildServiceProvider();
var factory = rootSp.GetRequiredService<AgentFactory>();
```

其余（RunAsync、EventBus 订阅）不变。

- [ ] **Step 2: 迁移 FeishuAdaptor**

读 `demo/FeishuAdaptor/Program.cs`，找到 `builder.Configuration.AddManInBlackSettings();`、`builder.Configuration.GetSection("Feishu").Bind(feishuSettings);`、`builder.Services.AddManInBlackFromConfiguration(builder.Configuration);`、`factory.RegisterPipeline("feishu", pipeline => pipeline.UseDefault());`。

改为（保持飞书从 builder.Configuration 读 + 流式 builder）：

```csharp
builder.Configuration.AddManInBlackSettings();   // 仍可保留：把 settings.json 加入 IConfiguration
builder.Services.AddManInBlack()
    .UseConfiguration(builder.Configuration)
    .AddFeishu(f => builder.Configuration.GetSection("Feishu").Bind(f))
    .AddPipeline("feishu", pipeline => pipeline.UseDefault());
```

> 若原代码用单独的 `feishuSettings` 变量做别的绑定，保留那段；新写法用 `.AddFeishu` 把同一个 section 绑进 `IOptions<FeishuSettings>`。确认后续读取飞书配置的地方改成从 `IOptions<FeishuSettings>` 取（或保留原变量）。

- [ ] **Step 3: 迁移 GitHubAdaptor**

读 `demo/GitHubAdaptor/Program.cs`，把 `builder.Services.AddManInBlackFromConfiguration(builder.Configuration);` 改为：

```csharp
builder.Services.AddManInBlack()
    .UseConfiguration(builder.Configuration);
```

若有 `RegisterPipeline` post-build 调用，迁到 `.AddPipeline(...)`。

- [ ] **Step 4: 全量构建**

Run: `dotnet build ManInBlack.slnx`
Expected: 成功。

- [ ] **Step 5: 运行全部测试**

Run: `dotnet test ManInBlack.slnx`
Expected: 全部 PASS。

- [ ] **Step 6: 提交**

```bash
git add demo/AgentConsole/Program.cs demo/FeishuAdaptor/Program.cs demo/GitHubAdaptor/Program.cs
git commit -m "♻️ demo 迁移到流式 builder API"
```

> 手动验证（可选）：`dotnet run --project demo/AgentConsole "你好"` 能正常流式输出即说明端到端打通。

---

## Task 9: 文档同步

**Files:**
- Modify: `docs/configuration-guide.md`
- Modify: `docs/quick-start.md`
- Modify: `docs/agent-factory-guide.md`

- [ ] **Step 1: configuration-guide.md 新增 Builder 章节**

在 `docs/configuration-guide.md` 中新增一节「流式 Builder（代码配置）」，包含：

- 完整链式示例（UseJson + AddProvider + AddAgent + AddPipeline + UseSandbox）。
- 合并语义说明：「委托覆盖 JSON，JSON 用 .UseJson() 显式载入；位置决定合并层」。
- 「对象重载」说明（`.AddProvider("default", new ProviderSettings{...})`）。
- 子 Builder 方法速查表（Provider/ModelChoice/Agent/Hook/McpServer/Storage）。
- 在原有「DI 注册方式对比」表新增一行：`AddManInBlack()` → 流式 builder（推荐）。
- 标注 `AddManInBlack(Action<ManInBlackOptions>)` 为 `[Obsolete]`。

- [ ] **Step 2: quick-start.md 第四步改为流式**

把 `docs/quick-start.md` 第四步的 `services.AddManInBlackFromSettings();` 示例改为：

```csharp
var services = new ServiceCollection();
services.AddManInBlack()
    .UseJson();   // 从 ~/.man-in-black/settings.json 读取
```

并把「手动配置（不使用 settings.json）」一节里的 `AddManInBlack(opt => { opt.ModelChoice = ... })` 改为：

```csharp
services.AddManInBlack()
    .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx").BaseUrl("https://api.deepseek.com"))
    .AddModelChoice("default", c => c.Provider("default").ModelId("deepseek-chat"));
```

同步删除/更新原「两者不可混用」的提示（现在统一为流式 + settings）。

- [ ] **Step 3: agent-factory-guide.md 更新 pipeline 注册**

把 `docs/agent-factory-guide.md` 中「管道注册方式」一节改为优先介绍 `.AddPipeline(...)`（DI 期），保留 `AgentFactory.RegisterPipeline` 作为「运行时动态注册逃生口」说明。

- [ ] **Step 4: 提交**

```bash
git add docs/configuration-guide.md docs/quick-start.md docs/agent-factory-guide.md
git commit -m "📝 同步流式 builder 配置文档"
```

---

## Self-Review（计划自检，执行前已做）

**Spec coverage：**
- 流式 Builder API → Task 2/3/4/5。
- 统一到 ManInBlackSettings 按序合并 → Task 1（引擎）+ Task 6（IConfigureOptions 落地）。
- 委托覆盖 JSON、JSON 显式载入 → Task 5（UseJson/UseConfiguration）+ 合并测试。
- Pipeline 收进 builder + 逃生口 → Task 3（AddPipeline + AgentFactory ctor + 保留 RegisterPipeline）。
- 外部扩展（FeishuAdaptor）→ Task 7。
- 旧入口保留 + [Obsolete] → Task 6。
- 子 Builder 对象重载 → Task 2/3/4。
- 校验复用 → Task 6 端到端测试覆盖 OptionsValidationException。
- 文档同步 → Task 9。

**Placeholder scan：** 无 TODO/TBD/「稍后实现」。Task 6 的 `AgentStorageOptions` 映射用独立 `AgentStorageOptionsConfigurer`（IConfigureOptions）正确实现，未留占位。

**Type consistency：**
- `IManInBlackContribution.Apply(ManInBlackSettings)` 全程一致（Task 1 定义，Task 2-6 使用）。
- `ManInBlackBuilder.AddContribution(IManInBlackContribution)` 一致。
- `PipelineRegistration(string, Func<AgentPipelineBuilder, AgentPipelineBuilder>)` 与 `AddPipeline`、`AgentFactory` 收集一致。
- `AgentFactory` ctor 新增 `IEnumerable<PipelineRegistration>`，`AgentFactoryTests.CreateFactory` 已在 Task 3 Step 6 同步补 `[]`。
- 子 Builder 的 `internal Settings` 访问器与扩展方法读取一致。
