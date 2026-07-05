# 工具额外参数注入改造为配置驱动 — 设计文档

- 日期: 2026-07-05
- 状态: 已批准
- 相关 commit: `711bb1d`(引入 `ToolIntentSchemaMiddleware`)
- 涉及模块: `src/ManInBlack.AI`、`demo/FeishuAdaptor`、`test/ManInBlack.AI.Tests`、`docs/`

## 1. 背景与问题

上一个 commit (`711bb1d`) 引入 `ToolIntentSchemaMiddleware`,运行时为每个工具的 JSON Schema
追加一个参数(如 `reason` / `purpose`),让 LLM 调用工具时说明意图,供 UI/日志展示。

当前实现有两个痛点:

1. **构造器传参,字面量重复。** 中间件用主构造器收 `paramName` / `paramDescription` / `required`
   三个参数,在 `demo/FeishuAdaptor/Program.cs` 里**手写了两遍**同样的字面量:

   ```csharp
   // feishu 管道
   .UseDefault(b => b.Use(
       new ToolIntentSchemaMiddleware("purpose", "用一句话讲述你调用这个工具是为了做什么。", required: true)))
   // sub-agent 管道
   .Use(new ToolIntentSchemaMiddleware("purpose", "用一句话讲述你调用这个工具是为了做什么。", required: true))
   ```

   改一处忘一处,且与代码库其余「从 `IOptions<ManInBlackSettings>` 读」的约定(`UseSandbox`、
   `FileIsolation` 等)不一致。

2. **命名不够贴切。** "Tool Intent" 暗示语义偏向"意图",而机制本身是通用的"给工具 Schema
   追加一个额外参数"。重命名为 **Tool Extra Parameter** 更准确。

## 2. 目标

- 中间件不再从构造器接收业务参数,改为从 `IOptions<ManInBlackSettings>` 读取。
- 提供**两种配置入口**:JSON(`"ToolExtraParameter"` 节)与流式扩展方法
  `AddToolExtraParameter(Action<ToolExtraParameterBuilder>)`,二者等价。
- demo 里字面量收敛到一处,中间件注册改为 `Use<ToolExtraParameterMiddleware>()`(无参)。
- 全链路重命名 `ToolIntent*` → `ToolExtraParameter*`。

## 3. 非目标(范围护栏)

- **不自动注册中间件进默认管道。** 是否启用、插入到哪条管道仍由 demo 显式 `Use<>()` 决定
  (中间件必须落在 `ToolsMiddleware` 之后、`UseSimple` 之前,需调用方自行保证顺序)。
- **不引入 `Enabled` 开关。** 想关就不 `Use<>()`。
- **不改变 `DecorateSchema` 的运行时行为**(幂等、同名校验跳过、`required` 数组处理逻辑不变)。
- 不做向后兼容保留(`ToolIntentSchemaMiddleware` 上个 commit 才引入,无外部消费者,直接改名)。

## 4. 现状梳理(已验证)

### 4.1 中间件注册与解析

- `AgentPipelineBuilder.Use<TMiddleware>()` 走 `sp.GetRequiredService<TMiddleware>()`,支持构造器注入。
- 中间件靠 `[ServiceRegister.Scoped]` 特性被源生成器 `ServiceRegistrationGenerator` 采集,
  经 `AddAutoRegisteredServices()` 注册为 Scoped(参见 `EventPublishingMiddleware` / `ToolsMiddleware`
  等既有写法)。
- 当前 `ToolIntentSchemaMiddleware` **未标注该特性、未进 DI**,只能 `new` 后 `Use(instance)`,
  故无法构造器注入 `IOptions`。改造后必须补 `[ServiceRegister.Scoped]`。

### 4.2 配置流入路径

`UseConfiguration(cfg)` → `configuration.Bind(loaded)` 把 JSON 绑成 `ManInBlackSettings`
(嵌套 POCO 按名自动绑定)→ 作为 contribution 注册
(`s => SettingsMerger.Merge(s, source)`)→ `ManInBlackSettingsBuilder`(实现 `IConfigureOptions`)
在 `IOptions<ManInBlackSettings>` 首次 resolve 时,按注册顺序对每个 contribution 调
`Apply(settings)`,在同一 `settings` 实例上累加 mutate。

**关键约束**:`SettingsMerger.Merge` 对每个字段**显式**处理。新增 `ToolExtraParameter` 字段
必须**同步在 Merge 加一行**,否则 JSON 值流不进最终 settings。

### 4.3 贡献优先级

贡献按注册顺序应用,标量/对象覆盖语义为「后应用的赢」。demo 调用顺序
`.UseConfiguration(...).AddToolExtraParameter(...)` → Merge 先跑(写 JSON 值),扩展后跑(覆盖为
代码值)。即**代码侧入口在配置之后调用时,代码值优先**。文档需写明此约定。

## 5. 设计

### 5.1 配置层 — `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs`

新增嵌套节,非空默认值,摆放风格对齐 `Storage`:

```csharp
public class ManInBlackSettings
{
    // ... 既有字段 ...

    /// <summary>
    /// 工具额外参数注入配置:运行时为每个工具的 JSON Schema 追加一个参数。
    /// </summary>
    public ToolExtraParameterSettings ToolExtraParameter { get; set; } = new();
}

/// <summary>
/// 工具额外参数注入的配置项。
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

### 5.2 Builder — `src/ManInBlack.AI/Configuration/SubBuilders/ToolExtraParameterBuilder.cs`(新增)

仿 `HookBuilder` 的扁平 setter 风格:

```csharp
namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 ToolExtraParameterSettings。
/// </summary>
public sealed class ToolExtraParameterBuilder
{
    internal ToolExtraParameterSettings Settings { get; } = new();

    /// <summary>设置追加参数名(默认 "reason")。</summary>
    public ToolExtraParameterBuilder ParamName(string paramName)
    { Settings.ParamName = paramName; return this; }

    /// <summary>设置追加参数的描述。</summary>
    public ToolExtraParameterBuilder ParamDescription(string description)
    { Settings.ParamDescription = description; return this; }

    /// <summary>设置是否必填。</summary>
    public ToolExtraParameterBuilder Required(bool required)
    { Settings.Required = required; return this; }
}
```

### 5.3 扩展方法 — `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs`

仿 `UseStorage`:

```csharp
/// <summary>
/// 配置工具额外参数注入(代码侧入口,等价于 settings.json 的 "ToolExtraParameter" 节)。
/// 在 UseConfiguration/UseJson 之后调用则以代码值为准。
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

### 5.4 合并器 — `src/ManInBlack.AI/Configuration/SettingsMerger.cs`

`Merge` 内新增一行(放在 `UseSandbox` 一类标量处理旁):

```csharp
target.ToolExtraParameter = source.ToolExtraParameter;
```

注:总覆盖语义对当前场景安全 —— `source.ToolExtraParameter` 始终非空(`configuration.Bind`
产出的对象及 `ToolExtraParameterSettings` 默认构造都保证非 null),且无其它 contribution
竞争该字段。

### 5.5 中间件 — `src/ManInBlack.AI/Middlewares/ToolExtraParameterMiddleware.cs`(重命名 + 改注入)

文件由 `ToolIntentSchemaMiddleware.cs` 重命名而来。主构造器改为注入 `IOptions`,加
`[ServiceRegister.Scoped]`。`HandleAsync` / `DecorateSchema` 逻辑保持不变,仅把对
`paramName` / `paramDescription` / `required` 的引用改为私有只读字段:

```csharp
using ManInBlack.AI.Abstraction.Attributes;   // ServiceRegister
// ... 其余 using ...

namespace ManInBlack.AI.Middlewares;

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
        // 不变:遍历 context.Options?.Tools,对 AIFunctionDeclaration 调 DecorateSchema
    }

    private AIFunctionDeclaration DecorateSchema(AIFunctionDeclaration original)
    {
        // 不变:幂等校验 + properties[paramName] = {type, description} + 按 _required 追加 required 数组
        // 仅把 paramName/paramDescription/required 引用换成 _paramName/_paramDescription/_required
    }
}
```

> 类文档注释同步更新:推荐注册方式改为 `Use<ToolExtraParameterMiddleware>()`,配置经
> `AddToolExtraParameter(...)` 或 `"ToolExtraParameter"` JSON 节。

### 5.6 demo — `demo/FeishuAdaptor/Program.cs`

字面量收敛到 `AddToolExtraParameter` 一处,中间件注册去字面量:

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

## 6. 数据流(端到端)

```
启动期:
  AddManInBlack()
    → UseConfiguration(cfg)            // 注册 contribution: Merge(s, jsonBound)
    → AddToolExtraParameter(...)       // 注册 contribution: s.ToolExtraParameter = builder值
    → AddPipeline(...)                 // 注册管道: Use<ToolExtraParameterMiddleware>()

  IOptions<ManInBlackSettings> 首次 resolve:
    ManInBlackSettingsBuilder.Configure(settings):
      1) Merge(settings, jsonBound)        → settings.ToolExtraParameter = JSON 值(或默认)
      2) 扩展 contribution                → settings.ToolExtraParameter = builder 值(覆盖)

  AddAutoRegisteredServices()         // 源生成器注册 ToolExtraParameterMiddleware 为 Scoped

请求期:
  管道 Build → Use<ToolExtraParameterMiddleware>() 从 DI 解析
    → 构造器注入 IOptions<ManInBlackSettings>,捕获 _paramName/_paramDescription/_required
  HandleAsync:
    → 遍历 context.Options.Tools,DecorateSchema 为每个工具 schema 追加参数
    → next() 进入 AgentLoop
```

## 7. 测试 — `test/ManInBlack.AI.Tests`(手写 fake,遵循项目约定)

新增测试文件,覆盖:

1. **中间件读取配置(自定义值)**:构造带 `ToolExtraParameter { ParamName="purpose", Required=true }`
   的 `IOptions<ManInBlackSettings>`,跑 middleware 后断言某工具 schema 的 `properties` 含
   `purpose`、`required` 数组含 `purpose`。
2. **中间件默认值(空 settings)**:`ManInBlackSettings()` 默认值,断言追加参数名为 `reason`、
   不在 `required` 中,且原 schema 字段保留。
3. **幂等**:同一 schema 跑两次,`properties[purpose]` 不重复构造。
4. **Builder 落地**:`new ToolExtraParameterBuilder().ParamName("x").ParamDescription("y").Required(true)`
   产出的 `Settings` 字段正确。

> 中间件测试需构造一个最小的 `AgentContext` 与带 `Tools` 的 `AgentContext.Options`。
> 若现有测试基础设施不易造,可参考既有 middleware 测试的造法;若无现成 fake,则新增最小 fake。

## 8. 文档同步

- `docs/middleware-guide.md`:「在 ToolsMiddleware 与 UseSimple 之间插入中间件」一节,
  注册示例改为 `Use<ToolExtraParameterMiddleware>()`,新增 `AddToolExtraParameter(...)` 与
  `"ToolExtraParameter"` JSON 节两种配置片段。
- `docs/configuration-guide.md`:新增 `ToolExtraParameter` 节说明(字段表 + JSON/扩展方法示例)。

## 9. 涉及文件清单

| 文件 | 动作 |
|---|---|
| `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs` | 加 `ToolExtraParameter` 属性 + `ToolExtraParameterSettings` 类 |
| `src/ManInBlack.AI/Configuration/SubBuilders/ToolExtraParameterBuilder.cs` | 新增 |
| `src/ManInBlack.AI/ManInBlackBuilderExtensions.cs` | 加 `AddToolExtraParameter` 扩展 |
| `src/ManInBlack.AI/Configuration/SettingsMerger.cs` | `Merge` 加一行 |
| `src/ManInBlack.AI/Middlewares/ToolIntentSchemaMiddleware.cs` → `ToolExtraParameterMiddleware.cs` | 重命名 + 改构造器注入 + 加 `[ServiceRegister.Scoped]` |
| `demo/FeishuAdaptor/Program.cs` | 注册改为 `Use<>()` + `AddToolExtraParameter(...)` |
| `test/ManInBlack.AI.Tests/.../ToolExtraParameterMiddlewareTests.cs` | 新增 |
| `docs/middleware-guide.md` | 更新示例 |
| `docs/configuration-guide.md` | 新增 `ToolExtraParameter` 节 |

## 10. 向后兼容

`ToolIntentSchemaMiddleware` 改名 + 改构造器为 breaking change。该类 commit `711bb1d` 引入,
仅 `demo/FeishuAdaptor` 内部消费,无外部引用,随本次一并迁移即可。git rename 应能识别文件移动。

## 11. 风险与备注

- **源生成器特性位置**:`[ServiceRegister.Scoped]` 需确认其命名空间
  (`ManInBlack.AI.Abstraction.Attributes.ServiceRegister` 的 `ScopedAttribute`)在中间件文件
  可见;实现期核对 using。
- **merge 顺序**:若调用方把 `AddToolExtraParameter` 放在 `UseConfiguration` 之前,JSON 会
  覆盖代码值。文档写明「配置入口后调用者赢」,demo 示例遵循「config → Add → pipelines」顺序。
- **`AgentContext.Options.Tools` 类型**:实现期核对 `AIFunctionDeclaration` / `ToolFunctionDeclaration`
  在当前 `Microsoft.Extensions.AI` 版本的真实 API(参考既有 `ToolsMiddleware` 用法),必要时调整
  `DecorateSchema` 的构造调用。
