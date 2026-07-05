# 工具额外参数注入(配置驱动)实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `ToolIntentSchemaMiddleware` 改造为从 `IOptions<ManInBlackSettings>` 读取参数的 `ToolExtraParameterMiddleware`,并提供 `AddToolExtraParameter(...)` 流式扩展与 `"ToolExtraParameter"` JSON 节两种配置入口。

**Architecture:** 新增 `ToolExtraParameterSettings` 嵌套节挂到 `ManInBlackSettings`;`ToolExtraParameterBuilder` + `AddToolExtraParameter` 扩展方法注册 contribution;`SettingsMerger` 增加一行让 JSON 值流入最终 settings;中间件改构造器注入 `IOptions<ManInBlackSettings>` 并加 `[ServiceRegister.Scoped]` 自动进 DI,demo 改为 `Use<ToolExtraParameterMiddleware>()`。

**Tech Stack:** .NET 10、`Microsoft.Extensions.AI`(`AIFunctionDeclaration` / `ToolFunctionDeclaration` / `ChatOptions`)、`Microsoft.Extensions.Options`(`IOptions<T>`)、xunit、手写 fake。

## Global Constraints

- 所有注释与文档使用中文。
- 提交信息使用 [gitmoji](https://gitmoji.dev/) 前缀;**禁止** `Co-authored-by` 尾部。
- 测试使用手写 fake,**不使用** mock 框架;断言用 xunit。
- 修改模块后必须同步更新 `docs/` 下对应文档。
- 测试工程无 `InternalsVisibleTo`,故 `internal` 成员不可直测,改走端到端 / 公开 API 覆盖。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs` | 持有 `ToolExtraParameter` 属性 + `ToolExtraParameterSettings` POCO | 修改 |
| `src/ManInBlack.AI/Configuration/SubBuilders/ToolExtraParameterBuilder.cs` | 流式构建 `ToolExtraParameterSettings` | 新增 |
| `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs` | `AddToolExtraParameter` 扩展方法 | 修改 |
| `src/ManInBlack.AI/Configuration/SettingsMerger.cs` | `Merge` 中拷贝 `ToolExtraParameter` | 修改 |
| `src/ManInBlack.AI/Middlewares/ToolIntentSchemaMiddleware.cs` → `ToolExtraParameterMiddleware.cs` | 读 `IOptions`、`[ServiceRegister.Scoped]`、Schema 追加参数 | 重命名 + 重写 |
| `demo/FeishuAdaptor/Program.cs` | 接入 `AddToolExtraParameter` + `Use<ToolExtraParameterMiddleware>()` | 修改 |
| `test/ManInBlack.AI.Tests/Configuration/ToolExtraParameterConfigTests.cs` | 配置入口端到端测试 | 新增 |
| `test/ManInBlack.AI.Tests/Middlewares/ToolExtraParameterMiddlewareTests.cs` | 中间件装饰/默认/幂等测试 | 新增 |
| `docs/middleware-guide.md` | 注册示例更新 | 修改 |
| `docs/configuration-guide.md` | 新增 `ToolExtraParameter` 节 | 修改 |

---

## Task 1: 配置面 —— POCO + Builder + 扩展方法 + Merger

**Files:**
- Modify: `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs`
- Create: `src/ManInBlack.AI/Configuration/SubBuilders/ToolExtraParameterBuilder.cs`
- Modify: `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs`
- Modify: `src/ManInBlack.AI/Configuration/SettingsMerger.cs`
- Test: `test/ManInBlack.AI.Tests/Configuration/ToolExtraParameterConfigTests.cs`

**Interfaces:**
- Produces: `ToolExtraParameterSettings { ParamName, ParamDescription, Required }`、`ManInBlackSettings.ToolExtraParameter`、`ToolExtraParameterBuilder`、`IManInBlackBuilder.AddToolExtraParameter(Action<ToolExtraParameterBuilder>)`。Task 2 的中间件依赖 `ManInBlackSettings.ToolExtraParameter`。

- [ ] **Step 1: 写失败测试**

创建 `test/ManInBlack.AI.Tests/Configuration/ToolExtraParameterConfigTests.cs`:

```csharp
using ManInBlack.AI;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class ToolExtraParameterConfigTests
{
    // 校验器要求至少一个 Provider+ModelChoice,这里给最小 seed 让 IOptions 能 resolve
    private static IManInBlackBuilder BuildWithDefaults(ServiceCollection services) =>
        services.AddManInBlack()
            .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-x"))
            .AddModelChoice("default", c => c.Provider("default").ModelId("gpt-4o"));

    [Fact]
    public void AddToolExtraParameter_AfterUseConfiguration_CodeValueWins()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "sk-x",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
            ["ToolExtraParameter:ParamName"] = "from-json"
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        BuildWithDefaults(services)
            .UseConfiguration(cfg)
            .AddToolExtraParameter(p => p.ParamName("purpose").Required(true));

        var settings = services.BuildServiceProvider()
            .GetRequiredService<IOptions<ManInBlackSettings>>().Value;

        Assert.Equal("purpose", settings.ToolExtraParameter.ParamName);
        Assert.True(settings.ToolExtraParameter.Required);
    }

    [Fact]
    public void UseConfiguration_BindsToolExtraParameterSection_FromJson()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Providers:default:Schema"] = "OpenAI",
            ["Providers:default:ApiKey"] = "sk-x",
            ["ModelChoices:default:ProviderName"] = "default",
            ["ModelChoices:default:ModelId"] = "gpt-4o",
            ["ToolExtraParameter:ParamName"] = "from-json",
            ["ToolExtraParameter:ParamDescription"] = "desc-from-json",
            ["ToolExtraParameter:Required"] = "true"
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        BuildWithDefaults(services).UseConfiguration(cfg);

        var settings = services.BuildServiceProvider()
            .GetRequiredService<IOptions<ManInBlackSettings>>().Value;

        Assert.Equal("from-json", settings.ToolExtraParameter.ParamName);
        Assert.Equal("desc-from-json", settings.ToolExtraParameter.ParamDescription);
        Assert.True(settings.ToolExtraParameter.Required);
    }
}
```

- [ ] **Step 2: 跑测试,确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ToolExtraParameterConfigTests"`
Expected: 编译失败(`ToolExtraParameter` 属性 / `AddToolExtraParameter` / `ToolExtraParameterBuilder` 均不存在)。

- [ ] **Step 3: 在 `ManInBlackSettings.cs` 加 POCO + 属性**

在 `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs` 的 `ManInBlackSettings` 类内(紧邻 `UseSandbox` 属性之后)新增属性:

```csharp
    /// <summary>
    /// 工具额外参数注入配置:运行时为每个工具的 JSON Schema 追加一个参数。
    /// </summary>
    public ToolExtraParameterSettings ToolExtraParameter { get; set; } = new();
```

在同一文件的命名空间内(`ManInBlack.AI.Configuration`)新增 POCO 类(放在 `ManInBlackSettings` 类定义之后、`StorageSettings` 之前的位置即可):

```csharp
/// <summary>
/// 工具额外参数注入的配置项。可经 settings.json 的 "ToolExtraParameter" 节
/// 或流式扩展 AddToolExtraParameter(...) 配置。
/// </summary>
public class ToolExtraParameterSettings
{
    /// <summary>追加的参数名。默认 "reason"。</summary>
    public string ParamName { get; set; } = "reason";

    /// <summary>追加参数的描述(LLM 可见)。</summary>
    public string ParamDescription { get; set; } =
        "Briefly explain what you intend to accomplish by calling this tool.";

    /// <summary>是否在 schema 的 required 数组中标记此参数。默认 false。</summary>
    public bool Required { get; set; }
}
```

- [ ] **Step 4: 新增 `ToolExtraParameterBuilder.cs`**

创建 `src/ManInBlack.AI/Configuration/SubBuilders/ToolExtraParameterBuilder.cs`,仿 `HookBuilder` 风格:

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 <see cref="ToolExtraParameterSettings"/>。
/// </summary>
public sealed class ToolExtraParameterBuilder
{
    internal ToolExtraParameterSettings Settings { get; } = new();

    /// <summary>设置追加参数名(默认 "reason")。</summary>
    public ToolExtraParameterBuilder ParamName(string paramName)
    { Settings.ParamName = paramName; return this; }

    /// <summary>设置追加参数的描述(LLM 可见)。</summary>
    public ToolExtraParameterBuilder ParamDescription(string description)
    { Settings.ParamDescription = description; return this; }

    /// <summary>设置是否在 schema 的 required 中标记此参数。</summary>
    public ToolExtraParameterBuilder Required(bool required)
    { Settings.Required = required; return this; }
}
```

- [ ] **Step 5: 在 `ManInBlackBuilderExtensions.cs` 加扩展方法**

在 `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs` 内(放在 `UseSandbox` 扩展方法之后)新增:

```csharp
    /// <summary>
    /// 配置工具额外参数注入(代码侧入口,等价于 settings.json 的 "ToolExtraParameter" 节)。
    /// 在 <see cref="UseConfiguration"/>/<see cref="UseJson"/> 之后调用则以代码值为准。
    /// </summary>
    public static IManInBlackBuilder AddToolExtraParameter(
        this IManInBlackBuilder builder,
        Action<ToolExtraParameterBuilder> configure)
    {
        var b = new ToolExtraParameterBuilder();
        configure(b);
        ((ManInBlackBuilder)builder).AddContribution(
            new ActionContribution(s => s.ToolExtraParameter = b.Settings));
        return builder;
    }
```

> 确认文件顶部已有 `using ManInBlack.AI.Configuration;`(`ActionContribution` / `ManInBlackBuilder` 在同命名空间,通常已就位)。若 `Action`/`Action<>'` 缺 using,补 `using System;`(一般已由 global using 提供)。

- [ ] **Step 6: 在 `SettingsMerger.cs` 的 `Merge` 内加一行**

在 `src/ManInBlack.AI/Configuration/SettingsMerger.cs` 的 `Merge` 方法内,紧邻 `target.UseSandbox = source.UseSandbox;` 之后新增:

```csharp
        target.ToolExtraParameter = source.ToolExtraParameter;
```

- [ ] **Step 7: 跑测试,确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ToolExtraParameterConfigTests"`
Expected: 2 passed。

- [ ] **Step 8: 提交**

```bash
git add src/ManInBlack.AI/Configuration/ManInBlackSettings.cs \
        src/ManInBlack.AI/Configuration/SubBuilders/ToolExtraParameterBuilder.cs \
        src/ManInBlack.AI/ManInBlackBuilderExtensions.cs \
        src/ManInBlack.AI/Configuration/SettingsMerger.cs \
        test/ManInBlack.AI.Tests/Configuration/ToolExtraParameterConfigTests.cs
git commit -m "✨ 新增 ToolExtraParameter 配置面(POCO/Builder/AddToolExtraParameter/Merger)"
```

---

## Task 2: 中间件重命名 + 配置驱动改造

**Files:**
- Rename: `src/ManInBlack.AI/Middlewares/ToolIntentSchemaMiddleware.cs` → `ToolExtraParameterMiddleware.cs`
- Test: `test/ManInBlack.AI.Tests/Middlewares/ToolExtraParameterMiddlewareTests.cs`

**Interfaces:**
- Consumes: `ManInBlackSettings.ToolExtraParameter` (Task 1)。
- Produces: `[ServiceRegister.Scoped]` `ToolExtraParameterMiddleware(IOptions<ManInBlackSettings>)`,经 `Use<ToolExtraParameterMiddleware>()` 解析。Task 3 的 demo 依赖此构造。

- [ ] **Step 1: 写失败测试**

创建 `test/ManInBlack.AI.Tests/Middlewares/ToolExtraParameterMiddlewareTests.cs`:

```csharp
using System.Text.Json.Nodes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class ToolExtraParameterMiddlewareTests
{
    // 种子 schema:含一个原参数 x,不含 required
    private const string SeedSchema =
        """{"type":"object","properties":{"x":{"type":"string"}}}""";

    private static AgentContext NewContext()
    {
        var tool = new ToolFunctionDeclaration("MyTool", "desc", SeedSchema);
        return new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            Options = new ChatOptions { Tools = [tool] }
        };
    }

    private static JsonObject SchemaOf(AITool tool) =>
        JsonNode.Parse(((AIFunctionDeclaration)tool).JsonSchema.GetRawText())!.AsObject();

    [Fact]
    public async Task Decorate_AppendsConfiguredParam_AndMarksRequired()
    {
        var settings = Options.Create(new ManInBlackSettings
        {
            ToolExtraParameter = new ToolExtraParameterSettings
            { ParamName = "purpose", ParamDescription = "why", Required = true }
        });
        var middleware = new ToolExtraParameterMiddleware(settings);
        var ctx = NewContext();

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var schema = SchemaOf(ctx.Options!.Tools[0]);
        Assert.True(schema["properties"]!.AsObject().ContainsKey("purpose"));
        Assert.Equal("why", schema["properties"]!["purpose"]!["description"]!.GetValue<string>());
        Assert.Contains("purpose",
            schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        // 原参数保留
        Assert.True(schema["properties"]!.AsObject().ContainsKey("x"));
    }

    [Fact]
    public async Task Decorate_UsesDefaults_WhenSettingsLeftDefault()
    {
        var middleware = new ToolExtraParameterMiddleware(Options.Create(new ManInBlackSettings()));
        var ctx = NewContext();

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var schema = SchemaOf(ctx.Options!.Tools[0]);
        Assert.True(schema["properties"]!.AsObject().ContainsKey("reason"));
        // 默认 Required=false → 不写 required 数组
        Assert.False(schema.ContainsKey("required"));
    }

    [Fact]
    public async Task Decorate_IsIdempotent_AcrossMultipleRuns()
    {
        var middleware = new ToolExtraParameterMiddleware(Options.Create(new ManInBlackSettings()));
        var ctx = NewContext();

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();
        var firstRaw = ((AIFunctionDeclaration)ctx.Options!.Tools[0]).JsonSchema.GetRawText();

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();
        var secondRaw = ((AIFunctionDeclaration)ctx.Options!.Tools[0]).JsonSchema.GetRawText();

        Assert.Equal(firstRaw, secondRaw);
    }
}
```

- [ ] **Step 2: 跑测试,确认失败**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ToolExtraParameterMiddlewareTests"`
Expected: 编译失败(`ToolExtraParameterMiddleware` 不存在)。

- [ ] **Step 3: 重命名文件(git mv 保留历史)**

Run:
```bash
git mv src/ManInBlack.AI/Middlewares/ToolIntentSchemaMiddleware.cs \
       src/ManInBlack.AI/Middlewares/ToolExtraParameterMiddleware.cs
```

- [ ] **Step 4: 重写中间件文件**

用以下内容**整体替换** `src/ManInBlack.AI/Middlewares/ToolExtraParameterMiddleware.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 运行时为每个工具的 JSON Schema 追加一个额外参数(如 reason/purpose),
/// 让 LLM 调用工具时说明意图,供 UI 或日志展示。
/// <para>
/// 参数从 <see cref="ManInBlackSettings.ToolExtraParameter"/> 读取,
/// 可经 settings.json 的 "ToolExtraParameter" 节或流式扩展
/// <c>AddToolExtraParameter(...)</c> 配置。
/// </para>
/// <para>
/// 必须注册在 <see cref="ToolsMiddleware"/> 之后、<see cref="AgentLoopMiddleware"/> 之前。
/// 典型:<c>UseDefault(b =&gt; b.Use&lt;ToolExtraParameterMiddleware&gt;())</c>。
/// </para>
/// <para>
/// 追加的参数不会出现在工具方法签名上,源生成器 handler 不会提取它,
/// 值会留在 <c>ToolExecuteContext.Arguments</c> 中,由 <c>AgentLifecycleFilter</c>
/// 随 <c>BeforeToolExecuteEvent.ArgumentsJson</c> 一起发布,供 UI 消费。
/// </para>
/// </summary>
[ServiceRegister.Scoped]
public class ToolExtraParameterMiddleware(IOptions<ManInBlackSettings> settings) : AgentMiddleware
{
    private readonly string _paramName = settings.Value.ToolExtraParameter.ParamName;
    private readonly string _paramDescription = settings.Value.ToolExtraParameter.ParamDescription;
    private readonly bool _required = settings.Value.ToolExtraParameter.Required;

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (context.Options?.Tools is { Count: > 0 } tools)
        {
            for (var i = 0; i < tools.Count; i++)
            {
                if (tools[i] is AIFunctionDeclaration decl)
                    tools[i] = DecorateSchema(decl);
            }
        }

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }

    private AIFunctionDeclaration DecorateSchema(AIFunctionDeclaration original)
    {
        var schemaNode = JsonNode.Parse(original.JsonSchema.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException(
                $"工具 '{original.Name}' 的 JsonSchema 不是有效的 JSON 对象。");

        // 确保 "properties" 节点存在
        if (!schemaNode.ContainsKey("properties"))
            schemaNode["properties"] = new JsonObject();

        var properties = schemaNode["properties"]!.AsObject();

        // 幂等:同名参数已存在则跳过
        if (properties.ContainsKey(_paramName))
            return original;

        properties[_paramName] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = _paramDescription,
        };

        if (_required)
        {
            if (!schemaNode.ContainsKey("required"))
                schemaNode["required"] = new JsonArray();
            schemaNode["required"]!.AsArray().Add(_paramName);
        }

        return new ToolFunctionDeclaration(
            original.Name,
            original.Description ?? string.Empty,
            schemaNode.ToJsonString(),
            original.ReturnJsonSchema?.GetRawText());
    }
}
```

- [ ] **Step 5: 跑测试,确认通过**

Run: `dotnet test test/ManInBlack.AI.Tests --filter "FullyQualifiedName~ToolExtraParameterMiddlewareTests"`
Expected: 3 passed。

- [ ] **Step 6: 构建主库,确认 `[ServiceRegister.Scoped]` 源生成无错**

Run: `dotnet build src/ManInBlack.AI`
Expected: 成功(源生成器把 `ToolExtraParameterMiddleware` 纳入 `AddAutoRegisteredServices()`)。

- [ ] **Step 7: 提交**

```bash
git add src/ManInBlack.AI/Middlewares/ToolExtraParameterMiddleware.cs \
        test/ManInBlack.AI.Tests/Middlewares/ToolExtraParameterMiddlewareTests.cs
git commit -m "♻️ ToolIntentSchemaMiddleware → ToolExtraParameterMiddleware(配置驱动)"
```

> 旧文件名经 `git mv` 移动,如工作流不便可用 `Write` 创建新文件 + `git rm` 旧文件代替;务必只保留 `ToolExtraParameterMiddleware.cs`。

---

## Task 3: Feishu demo 接入

**Files:**
- Modify: `demo/FeishuAdaptor/Program.cs`

**Interfaces:**
- Consumes: `AddToolExtraParameter` (Task 1)、`ToolExtraParameterMiddleware` (Task 2)。

- [ ] **Step 1: 改 `Program.cs` 的两处注册**

在 `demo/FeishuAdaptor/Program.cs` 找到 `AddManInBlack()` 链(约第 68 行起)。

把 `.UseConfiguration(builder.Configuration)` 之后、`.AddFeishu(...)` 之前,新增 `.AddToolExtraParameter(...)`;并把两条管道的 `new ToolIntentSchemaMiddleware(...)` 替换为 `Use<ToolExtraParameterMiddleware>()`。最终该段形如:

```csharp
builder.Services.AddManInBlack()
    .UseConfiguration(builder.Configuration)
    .AddToolExtraParameter(p => p
        .ParamName("purpose")
        .ParamDescription("用一句话讲述你调用这个工具是为了做什么。")
        .Required(true))
    .AddFeishu(f => builder.Configuration.GetSection("Feishu").Bind(f))
    .AddPipeline("feishu", pipeline => pipeline
        .UseDefault(b => b.Use<ToolExtraParameterMiddleware>()))
    .AddPipeline("sub-agent", b => b
        .Use<EventPublishingMiddleware>()
        .Use<ToolsMiddleware>()
        .Use<ToolExtraParameterMiddleware>()
        .UseSimple());
```

> 若 `Program.cs` 顶部已有 `using ManInBlack.AI.Middlewares;`(原代码用了 `ToolIntentSchemaMiddleware`/`EventPublishingMiddleware`/`ToolsMiddleware`),则无需新增 using;`AddToolExtraParameter` 由 `ManInBlack.AI` 命名空间的扩展方法提供,确认已有 `using ManInBlack.AI;`。

- [ ] **Step 2: 构建 demo 确认无字面量残留、编译通过**

Run: `dotnet build demo/FeishuAdaptor`
Expected: 成功。

同时用 grep 确认旧符号已清空:
Run: `git grep -n "ToolIntentSchemaMiddleware"` (在仓库根)
Expected: 无输出(或仅命中 docs/ 待 Task 4 处理)。

- [ ] **Step 3: 提交**

```bash
git add demo/FeishuAdaptor/Program.cs
git commit -m "🔧 Feishu demo 接入 AddToolExtraParameter + Use<ToolExtraParameterMiddleware>()"
```

---

## Task 4: 文档同步

**Files:**
- Modify: `docs/middleware-guide.md`
- Modify: `docs/configuration-guide.md`

**Interfaces:** 无(纯文档)。

- [ ] **Step 1: 更新 `docs/middleware-guide.md`**

找到「在 ToolsMiddleware 与 UseSimple 之间插入中间件」一节(上一 commit 引入)。把代码示例从:

```csharp
builder.UseDefault(b => b.Use(
    new ToolIntentSchemaMiddleware("reason", "Briefly explain what you intend to accomplish.", required: true)));
```

改为:

```csharp
// settings.json:"ToolExtraParameter": { "ParamName": "reason", "Required": true }
// 或代码侧:
builder.UseDefault(b => b.Use<ToolExtraParameterMiddleware>());
```

并补一段「配置入口」说明(两种等价入口):

```markdown
`ToolExtraParameterMiddleware` 的参数从 `ManInBlackSettings.ToolExtraParameter` 读取,
有两种等价配置入口:

**JSON**(`settings.json` / `appsettings.json`):

```json
"ToolExtraParameter": {
  "ParamName": "purpose",
  "ParamDescription": "用一句话讲述你调用这个工具是为了做什么。",
  "Required": true
}
```

**流式扩展**(在 `AddManInBlack()` 链上,`UseConfiguration`/`UseJson` 之后调用则代码值优先):

```csharp
builder.Services.AddManInBlack()
    .UseConfiguration(builder.Configuration)
    .AddToolExtraParameter(p => p
        .ParamName("purpose")
        .ParamDescription("用一句话讲述你调用这个工具是为了做什么。")
        .Required(true));
```
```

> 顺带把该文件中其余出现的 `ToolIntentSchemaMiddleware` 字样全部替换为 `ToolExtraParameterMiddleware`。

- [ ] **Step 2: 更新 `docs/configuration-guide.md`**

在该文档合适的位置(与其它 settings 节说明并列,如 `Storage` / `UseSandbox` 附近)新增一节:

````markdown
### ToolExtraParameter

运行时为每个工具的 JSON Schema 追加一个额外参数,让 LLM 调用工具时说明意图,供 UI/日志展示。
需配合在管道中注册 `ToolExtraParameterMiddleware`(位于 `ToolsMiddleware` 之后)。

| 字段 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `ParamName` | string | `"reason"` | 追加的参数名 |
| `ParamDescription` | string | `"Briefly explain what you intend to accomplish by calling this tool."` | 参数描述(LLM 可见) |
| `Required` | bool | `false` | 是否在 schema 的 `required` 中标记 |

**JSON 示例:**

```json
"ToolExtraParameter": {
  "ParamName": "purpose",
  "ParamDescription": "用一句话讲述你调用这个工具是为了做什么。",
  "Required": true
}
```

**代码示例:** `AddManInBlack().UseConfiguration(cfg).AddToolExtraParameter(p => p.ParamName("purpose").Required(true))`。

> 在 `UseConfiguration`/`UseJson` 之后调用 `AddToolExtraParameter` 时,代码值覆盖 JSON。
````

- [ ] **Step 3: 确认全仓无 `ToolIntentSchema` 残留**

Run: `git grep -n "ToolIntent"`
Expected: 无输出。

- [ ] **Step 4: 提交**

```bash
git add docs/middleware-guide.md docs/configuration-guide.md
git commit -m "📝 更新中间件/配置文档:ToolExtraParameter"
```

---

## Task 5: 全量验证

- [ ] **Step 1: 全量构建**

Run: `dotnet build ManInBlack.slnx`
Expected: 成功,0 error。

- [ ] **Step 2: 全量测试**

Run: `dotnet test test/ManInBlack.AI.Tests`
Expected: 全部通过(含新增的 `ToolExtraParameterConfigTests` 2 个 + `ToolExtraParameterMiddlewareTests` 3 个)。

- [ ] **Step 3: 确认 Feishu demo 仍可构建**

Run: `dotnet build demo/FeishuAdaptor`
Expected: 成功。

> 无需提交(本任务仅验证)。

---

## 自审记录

**1. Spec 覆盖:** spec 第 5 节各子项对应:
- 5.1 配置层 → Task 1 Step 3 ✓
- 5.2 Builder → Task 1 Step 4 ✓
- 5.3 扩展方法 → Task 1 Step 5 ✓
- 5.4 合并器 → Task 1 Step 6 ✓
- 5.5 中间件 → Task 2 ✓
- 5.6 demo → Task 3 ✓
- 第 7 节测试 → Task 1(2 配置)+ Task 2(3 中间件)✓
- 第 8 节文档 → Task 4 ✓

**2. 占位符扫描:** 无 TBD/TODO;所有代码步骤均含完整可编译代码。

**3. 类型一致性:** `ToolExtraParameterSettings`、`ToolExtraParameterBuilder`、`AddToolExtraParameter`、`ToolExtraParameterMiddleware` 命名跨任务一致;`ParamName`/`ParamDescription`/`Required` 字段名跨 POCO/Builder/JSON/测试一致。

**4. 已验证的运行时事实:**
- `[ServiceRegister.Scoped]` → `using ManInBlack.AI.Abstraction.Attributes;`(对齐 `EventPublishingMiddleware`)
- `ToolFunctionDeclaration : AIFunctionDeclaration`,4 参构造 `(name, description, jsonSchema, returnJsonSchema?)`
- `Options.Create(new ManInBlackSettings{...})` 为既有 fake 模式
- 解析 `IOptions<ManInBlackSettings>.Value` 触发校验,测试须 seed Provider+ModelChoice
- 测试工程无 `InternalsVisibleTo`,Builder 的 `internal Settings` 经扩展方法端到端覆盖
