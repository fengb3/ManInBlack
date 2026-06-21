# 配置 Builder 改造设计：流式委托 + JSON

- 日期：2026-06-22
- 状态：待评审
- 作者：Bohan Feng（与 Claude 协同设计）

## 背景与问题

ManInBlack 当前的配置系统存在**双轨不对等**：

- `ManInBlackSettings`（绑定 JSON）：覆盖 Providers / ModelChoices / Agents / Hooks / McpServers / Feishu / Storage / UseSandbox —— **全量**。
- `ManInBlackOptions`（委托 `AddManInBlack(Action<ManInBlackOptions>)`）：只有 `ModelChoice`(单个) / Storage / UseSandbox —— **极简**。

三个 DI 入口（`AddManInBlackFromSettings`、`AddManInBlackFromConfiguration`、`AddManInBlack(Action<>)`）最终都汇到 `AddManInBlack(Action<ManInBlackOptions>)`。结果是：**Providers 字典、多 ModelChoice、Agent 定义、Hooks、McpServers 这些都没法用委托配置**；Pipeline 还得等 `BuildServiceProvider` 之后单独调 `factory.RegisterPipeline(...)`（quick-start 文档专门提醒过「要在 RunAsync 之前调用」）。

目标：引入一个流式 Builder，让委托能配置 JSON 能配置的一切，两者并存、可叠加。

## 目标 / 非目标

**目标**

- 提供流式 Builder API：`services.AddManInBlack().AddProvider(...).AddAgent(...).UseJson()...`
- 委托与 JSON 统一到同一个内部模型 `ManInBlackSettings`，按注册顺序、同名后者覆盖前者合并。
- Pipeline 注册收进 Builder（DI 期），消除「忘记 post-build 注册」的坑。
- Builder 可被外部程序集扩展（FeishuAdaptor 自带 `.AddFeishu(...)`）。
- 旧三个 DI 入口保留，向后兼容。

**非目标**

- 不改变运行时执行模型（洋葱中间件、AgentFactory.RunAsync 语义不变）。
- 不引入新配置格式（仍是 JSON + 代码委托）。
- 不做远程/动态配置下发。

## 关键决策（已与用户确认）

1. **API 形态 = 流式 Builder**：`AddManInBlack()` 返回 `IManInBlackBuilder`，链式调用。已否决：单一 Options 大委托（不直观）、独立 Builder 对象 + 显式 `.Build()`（样板多、易忘）。
2. **合并语义 = 委托覆盖 JSON，JSON 显式载入**：`AddManInBlack()` 默认不碰文件；`.UseJson()` 才载入 `~/.man-in-black/settings.json`（缺失则创建默认，沿用现有 `EnsureSettingsFile` 行为）。`.UseJson()` 位置任意，位置决定合并层（通常放链首）。
3. **Pipeline = 收进 Builder**，保留 `AgentFactory.RegisterPipeline` 作为运行时动态注册的逃生口。
4. **Feishu = 不进核心**：在 FeishuAdaptor 项目里加 `AddFeishu(this IManInBlackBuilder, ...)` 扩展方法。因此核心 `IManInBlackBuilder` 必须暴露 `IServiceCollection Services`。
5. **旧入口 = 全保留**：前两个重写为薄封装，`AddManInBlack(Action<ManInBlackOptions>)` 标 `[Obsolete]` 进兼容期。

## 公开 API

### 核心 Builder

```csharp
services.AddManInBlack()                       // 返回 IManInBlackBuilder
    .UseJson()                                 // 载入 ~/.man-in-black/settings.json（缺失则创建默认）
    .UseConfiguration(builder.Configuration)    // Web 场景：复用已有 IConfiguration
    .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx").BaseUrl("..."))
    .AddModelChoice("default", c => c.Provider("default").ModelId("gpt-4o"))
    .AddAgent("console-agent", a => a
        .Instruction("你是AI助手")
        .Pipeline("default")
        .SubAgents("sub-agent")
        .ModelChoice("default"))
    .AddPipeline("default", b => b.UseDefault())
    .AddHook(h => h.On("before_run").Run("echo hi"))
    .AddMcpServer("tavily", m => m.Endpoint("https://mcp.tavily.com/mcp").Header("Authorization", "Bearer xxx"))
    .UseStorage(s => s.RootPath("/data/mib").Workspace(w => w.Mode(WorkspaceMode.CustomPath)))
    .UseSandbox();
```

每个 `.AddXxx` 同时提供**对象重载**：

```csharp
.AddProvider("default", new ProviderSettings { Schema = "OpenAI", ApiKey = "sk-xxx" })
.AddAgent("console-agent", new AgentDefinition { Instruction = "...", PipelineName = "default" })
```

### 子 Builder（流式配置单元）

| 子 Builder | 方法 | 落点 |
|---|---|---|
| `ProviderBuilder` | `.Schema` / `.ApiKey` / `.BaseUrl` | `ProviderSettings` |
| `ModelChoiceBuilder` | `.Provider(name)` / `.ModelId` | `ModelChoiceSettings` |
| `AgentBuilder` | `.Description` / `.Instruction` / `.Pipeline` / `.SubAgents` / `.ModelChoice` | `AgentDefinition` |
| `HookBuilder` | `.On(event)` / `.Run(script)` | `HookSettings` |
| `McpServerBuilder` | `.Command` / `.Arguments` / `.Endpoint` / `.Header` / `.Transport` 等 | `McpServerSettings` |

子 Builder 末尾隐式 `Build()` 出对应的 settings 对象（无需调用者显式调用）。

### 外部扩展（FeishuAdaptor）

```csharp
// 定义在 FeishuAdaptor 项目内
public static class FeishuBuilderExtensions
{
    public static IManInBlackBuilder AddFeishu(
        this IManInBlackBuilder builder, Action<FeishuSettings> configure)
    {
        builder.Services.Configure(configure);
        return builder;
    }
}
```

`FeishuSettings` 类型保留在核心库（POCO），扩展方法在适配器项目。运行时适配器仍通过 `IOptions<FeishuSettings>` 读取。

## 内部模型与合并机制

### 贡献（Contribution）与「即时注册」的分工

Builder 方法做**两类**操作，必须区分时机：

**A. 即时注册（builder 方法直接写 IServiceCollection）** —— 凡是 `AgentFactory` 构造时通过 `IEnumerable<T>` 收集的类型，必须在 ServiceProvider 构建前就注册好，因此由 builder 方法**即时**注册为单例：

- `.AddAgent("name", a)` / `.UseJson()` 里的每个 agent → 注册 `AgentDefinition` 单例。
- `.AddPipeline("name", r)` → 注册 `PipelineRegistration` 单例。
- `.UseJson()` 读文件发生在流式链上（文件在磁盘），即时遍历其 Agents 注册单例。

**B. 懒合并（Contribution）** —— 供 `IOptions<ManInBlackSettings>` 合并与 `ValidateManInBlackSettings` 校验用的内容，走贡献，在 IOptions 首次 resolve 时应用：

```csharp
namespace ManInBlack.AI.Configuration;

internal interface IManInBlackContribution
{
    void Apply(ManInBlackSettings settings);
}
```

- `.UseJson()` → 把文件的 Providers/ModelChoices/Agents/Hooks/McpServers/Storage/UseSandbox/Feishu 全部写进 settings。
- `.UseConfiguration(cfg)` → 绑定 cfg 到 settings。
- `.AddProvider("default", p)` → `settings.Providers["default"] = p`。
- `.AddAgent("name", a)` → `settings.Agents["name"] = ...`（**同时**走 A 注册 AgentDefinition 单例）。
- `.UseSandbox()` → `settings.UseSandbox = true`。
- `.UseStorage(...)` → `settings.Storage = ...`。

> Pipeline 注册（A）**不**进 settings——它没有 JSON 对应字段，纯运行时概念，直接以单例形式交给 AgentFactory。因此 Contribution.Apply 不接收 PipelineRegistry。

`IManInBlackBuilder` 仅暴露 `Services`（外部扩展如 Feishu 只需要它）：

```csharp
public interface IManInBlackBuilder
{
    IServiceCollection Services { get; }
}
```

具体实现类 `ManInBlackBuilder` 另有一个 **`internal`** 的 `Add(IManInBlackContribution)` 方法，供同程序集内的流式扩展方法（`AddProvider` 等）调用。这样 `IManInBlackContribution` 保持 internal，而 public 接口不泄漏内部类型。

### 合并与解析

一个 `ManInBlackSettingsBuilder` 单例在 ctor 收集 `IEnumerable<IManInBlackContribution>`，**首次访问时**按注册顺序应用到一份空的 `ManInBlackSettings`：

- **字典类型**（Providers / ModelChoices / Agents / McpServers）：按 key 覆盖，不存在的 key 保留。
- **列表类型**（Hooks）：累加（不去重）。
- **标量**（UseSandbox 等）：后者覆盖前者。

产物经 `IConfigureOptions<ManInBlackSettings>` 暴露给 `IOptions<ManInBlackSettings>`，从而：

- per-agent `ModelChoiceName` 解析（`AgentFactory.RunAsync` 内 `GetModelChoice(name)`）继续工作。
- `ValidateManInBlackSettings`（作为 `IValidateOptions<ManInBlackSettings>`）在 Configure 之后运行，对合并后的整体做校验（SubAgents 引用、自引用、Provider/ModelChoice 完整性）。

`IConfigureOptions` 在 `IValidateOptions` 之前运行，保证贡献先合并、后校验。

### Agent 定义与 AgentFactory

- Agent 定义以 `AgentDefinition` 单例形式**即时注册**（见上文 A），`AgentFactory` 构造对 `IEnumerable<AgentDefinition>` 的收集**不变**。
- `.AddAgent` / `.UseJson` 同时（通过 B 贡献）把 Agent 配置写入合并后的 `settings.Agents`，仅供校验使用。A 与 B 在同一 builder 方法里成对执行，不存在漂移。
- 手动 `services.AddAgentDefinition(def)` 保留为逃生口（跳过校验）。

### Pipeline 集成

- 新增 `PipelineRegistration { string Name; Func<AgentPipelineBuilder, AgentPipelineBuilder> Resolver; }` 单例，由 `.AddPipeline` **即时注册**（见 A）。
- `AgentFactory` 构造**新增** `IEnumerable<PipelineRegistration>` 参数，与内置 `default`/`simple` 合并进 `_pipelineResolvers`（用户定义的同名覆盖内置）。`IEnumerable<AgentDefinition>` 参数保留不变。
- `AgentFactory.RegisterPipeline` 保留，作为运行时动态注册的逃生口。

## 向后兼容

| 旧入口 | 新实现 |
|---|---|
| `AddManInBlackFromSettings(Action<ManInBlackOptions>? configure)` | `services.AddManInBlack().UseJson()`，`configure` 透传到兼容 shim |
| `AddManInBlackFromConfiguration(IConfiguration, Action<ManInBlackOptions>? configure)` | `services.AddManInBlack().UseConfiguration(configuration)` |
| `AddManInBlack(Action<ManInBlackOptions>)` | 标 `[Obsolete("改用 services.AddManInBlack().AddProvider/... 流式 API")]`，内部把 `ModelChoice`/`Storage`/`UseSandbox` 映射到对应贡献 |

`ManInBlackOptions` 类型保留（兼容期），新代码不再使用。

## 覆盖范围（对等核对）

| 配置项 | 委托方法 | JSON 字段 |
|---|---|---|
| Provider | `.AddProvider` | `Providers` |
| ModelChoice | `.AddModelChoice` | `ModelChoices` |
| Agent | `.AddAgent` | `Agents` |
| Pipeline | `.AddPipeline`（新增） | —— |
| Hook | `.AddHook` | `Hooks` |
| McpServer | `.AddMcpServer` | `McpServers` |
| Storage | `.UseStorage` | `Storage` |
| Sandbox | `.UseSandbox` | `UseSandbox` |
| Feishu | FeishuAdaptor `.AddFeishu` | `Feishu`（核心库 POCO，由 `.UseConfiguration` 绑定） |

## 测试计划（手写 fake，遵循 CLAUDE.md）

新增测试覆盖：

1. **纯委托**：不调 `.UseJson()`，全用 `.AddProvider/.AddModelChoice/.AddAgent`，能跑通 `RunAsync`。
2. **JSON + 委托合并**：`.UseJson()` 提供 default provider/agent，委托追加新的 + 覆盖同名的，断言合并结果。
3. **委托覆盖 JSON 同名 key**：`.UseJson()` 后 `.AddProvider("default", ...)`，验证 ApiKey/BaseUrl 为委托值。
4. **顺序敏感**：`.AddProvider("default", A)` 后 `.UseJson()` 含 default=B，验证最终为 B（JSON 在后则 JSON 赢）。
5. **Pipeline 收进 builder**：`.AddPipeline("custom", b => ...)`，`RunAsync` 用该 pipeline，断言中间件生效。
6. **校验**：委托定义的 Agent 引用不存在的 SubAgent，断言 `ValidateManInBlackSettings` 报错。
7. **旧入口仍工作**：`AddManInBlackFromSettings` / `AddManInBlackFromConfiguration` / `AddManInBlack(Action<>)` 行为不变。
8. **Feishu 扩展**：`.AddFeishu(...)` 后 `IOptions<FeishuSettings>` 取到值。

端到端：把 `demo/AgentConsole`、`demo/FeishuAdaptor`、`demo/GitHubAdaptor` 迁移到新 API，确认编译 + 运行。

## 文档同步（遵循 CLAUDE.md 约定）

修改模块后同步更新：

- `docs/configuration-guide.md`：新增 Builder 章节（流式 API、合并语义、`.UseJson()` 用法）、对比表加入新入口、标注旧入口 `[Obsolete]`。
- `docs/quick-start.md`：第四步代码示例改用流式 Builder；「手动配置」一节更新为 `.AddProvider(...)` 写法。
- `docs/agent-factory-guide.md`：Pipeline 注册改为 `.AddPipeline(...)`（DI 期），保留 `RegisterPipeline` 逃生口说明。

## 风险与未决

- **合并时序**：贡献在 `IOptions<ManInBlackSettings>` 首次 resolve 时合并；若有代码在 ServiceProvider 构建后立即读 settings（而非通过 IOptions），需统一走 IOptions。实现时需审计。
- **`AddManInBlackFromConfiguration` 的 `services.Configure<ManInBlackSettings>(configuration)`**：新实现改为 `.UseConfiguration(cfg)` 贡献，行为等价但路径不同；需测试覆盖 reloadOnChange 场景（IOptionsMonitor）。
- **FeishuSettings 留在核心库**：虽是适配器概念，但移动会破坏现有绑定；暂留，后续可单独清理。
