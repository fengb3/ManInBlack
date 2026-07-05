# 配置指南

本文档介绍 ManInBlack 的配置系统，包括 settings.json 结构、DI 注册方式、文件变更跟踪和配置校验。

---

## settings.json

所有配置统一在 `~/.man-in-black/settings.json`，首次运行自动创建默认文件。

```json
{
  "Providers": {
    "default": {
      "Schema": "OpenAI",
      "ApiKey": "sk-xxx",
      "BaseUrl": "https://api.openai.com"
    },
    "deepseek": {
      "Schema": "OpenAI",
      "ApiKey": "sk-yyy",
      "BaseUrl": "https://api.deepseek.com"
    }
  },
  "ModelChoices": {
    "default": {
      "ProviderName": "default",
      "ModelId": "gpt-4o"
    },
    "deepseek-chat": {
      "ProviderName": "deepseek",
      "ModelId": "deepseek-chat"
    }
  },
  "Agents": {
    "translator": {
      "Description": "翻译专家，擅长将文本翻译成各种语言",
      "Instruction": "你是一个翻译专家。用户会给你一段文本或一个文件路径，你读取文件内容后将其翻译成自然流畅的目标语言，不需要任何额外解释。",
      "PipelineName": "sub-agent",
      "ModelChoiceName": "deepseek-chat"
    },
    "console-agent": {
      "Instruction": "你是一个AI助手。你可以通过工具执行系统命令来帮助用户完成任务。请用中文回复。",
      "PipelineName": "default",
      "SubAgents": ["translator"]
    }
  },
  "UseSandbox": false,
  "Feishu": {
    "AppId": "",
    "AppSecret": "",
    "VerificationToken": "",
    "ApiBaseUrl": "https://open.feishu.cn/"
  }
}
```

### Providers 字段

| 字段      | 必填 | 说明                                                        |
| --------- | ---- | ----------------------------------------------------------- |
| `Schema`  | 是   | 协议类型：`"OpenAI"` / `"Anthropic"` / `"Gemini"`          |
| `ApiKey`  | 是   | API 密钥，启动时校验非空                                    |
| `BaseUrl` | 否   | 自定义地址，省略则使用 Schema 对应的默认值                   |

### ModelChoices 字段

| 字段           | 必填 | 说明                                                |
| -------------- | ---- | --------------------------------------------------- |
| `ProviderName` | 是   | 引用 Providers 中的 key                             |
| `ModelId`      | 是   | 模型标识符，如 `gpt-4o`、`deepseek-chat`            |

`ModelChoices` 必须包含 `"default"` 条目，启动时校验。

### Agents 字段

| 字段              | 必填 | 说明                                                                        |
| ----------------- | ---- | --------------------------------------------------------------------------- |
| `Description`     | 否   | Agent 描述，用于子 Agent 委托时的提示词生成                                  |
| `Instruction`     | 否   | 系统提示词                                                                  |
| `PipelineName`    | 否   | 管道名称，决定使用哪套中间件组合。默认 `"default"`                           |
| `SubAgents`       | 否   | 可委托的子 Agent 名称列表（对应 Agents 字典中的 key）                       |
| `ModelChoiceName` | 否   | 引用的 ModelChoice 名称。不填则使用全局默认 ModelChoice                      |

Agents 为字典结构，键即为 Agent 名称（唯一标识）。通过 `AddManInBlackFromSettings()`、`AddManInBlackFromConfiguration()` 或流式 Builder 的 `.UseJson()` / `.UseConfiguration()` 加载时，会自动注册为 `AgentDefinition`。Pipeline 可通过 `.AddPipeline(...)` 在 DI 期注册（见下文）。

---

## DI 注册方式

### 方式一：AddManInBlack()（流式 Builder，推荐）

`services.AddManInBlack()` 返回 `IManInBlackBuilder`，支持链式配置。默认不读取任何文件，需手动链入配置源。

```csharp
var services = new ServiceCollection();
services.AddManInBlack()
    .UseJson()                                                    // 载入 ~/.man-in-black/settings.json（缺失则创建默认）
    .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx").BaseUrl("https://api.openai.com"))
    .AddModelChoice("default", c => c.Provider("default").ModelId("gpt-4o"))
    .AddAgent("my-agent", a => a.Instruction("你是一个AI助手").Pipeline("default"))
    .AddPipeline("custom", builder => builder.Use<MyMiddleware>().UseDefault())
    .UseSandbox();
```

> **注意：** 也可以不链入 `.UseJson()`，完全用代码配置。`.UseJson()` 显式载入 `~/.man-in-black/settings.json`，其位置决定合并层——放链首则后续的 `.AddProvider()` 等委托会覆盖 JSON 中的同名条目。

#### 合并语义

配置源按链式调用顺序逐层合并。每层可以是 JSON 文件（`.UseJson()`）或 `IConfiguration`（`.UseConfiguration(cfg)`），也可以是委托（`.AddProvider()` 等子 Builder）。规则：

- **同名 key 覆盖**：后注册的覆盖先注册的（按调用顺序）。
- **不同 key 累加**：字典类型（Providers、ModelChoices、Agents 等）中不同名称的条目会累积。
- **`.UseJson()` 显式载入**：不自动读取文件，需手动链入。缺失 `settings.json` 时自动创建默认文件。

#### 对象重载

每个 `.AddXxx` 方法都有两个重载——委托形式和对象形式：

```csharp
// 委托形式
.AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx").BaseUrl("https://api.openai.com"))

// 对象形式
.AddProvider("default", new ProviderSettings { Schema = "OpenAI", ApiKey = "sk-xxx", BaseUrl = "https://api.openai.com" })
```

`AddModelChoice`、`AddAgent`、`AddMcpServer` 同理支持对象重载。

#### 子 Builder 方法速查

| 子 Builder         | 关键方法                                                                 | 说明               |
| ------------------ | ------------------------------------------------------------------------ | ------------------ |
| `ProviderBuilder`  | `.Schema()` / `.ApiKey()` / `.BaseUrl()`                                | AI 提供商配置      |
| `ModelChoiceBuilder` | `.Provider()` / `.ModelId()`                                          | 模型选择配置       |
| `AgentBuilder`     | `.Description()` / `.Instruction()` / `.Pipeline()` / `.SubAgents()` / `.ModelChoice()` | Agent 定义配置  |
| `HookBuilder`      | `.Name()` / `.HookPoint()` / `.Run()` / `.ToolName()` / `.TimeoutMs()` / `.Enabled()` | 钩子配置    |
| `McpServerBuilder` | `.Transport()` / `.Command()` / `.Arguments()` / `.Endpoint()` / `.Header()` / `.Enabled()` | MCP 服务器配置 |
| `StorageBuilder`   | `.RootPath()` / `.Workspace(w => w.Mode(WorkspaceMode.CustomPath).CustomPath(...))` | 存储与工作空间配置 |

> **存储说明：** `RootPath`（默认 `~/.man-in-black`）下存放 SQLite 数据库文件 `maninblack.db`，无需新增配置键。旧的 `sessions/` 和 `users/` 子目录不再产生新数据（仅一次性迁移工具读取）。详见 [存储指南](./storage-guide.md)。

### 方式二：AddManInBlackFromSettings（控制台 / 测试）

自动从 `~/.man-in-black/settings.json` 加载配置并注册所有服务：

```csharp
services.AddManInBlackFromSettings();
```

内部等价于 `AddManInBlack().UseJson()`，行为与手动链式调用完全相同。

### 方式三：AddManInBlackFromConfiguration（WebApplicationBuilder）

将配置源添加到宿主的 `IConfiguration`，适合 ASP.NET Core 等已有宿主配置的场景：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 将 settings.json 加入宿主配置（启用 reloadOnChange）
builder.Configuration.AddManInBlackSettings();

// 读取飞书配置
var feishuSettings = new FeishuSettings();
builder.Configuration.GetSection("Feishu").Bind(feishuSettings);

// 注册 ManInBlack 服务
builder.Services.AddManInBlackFromConfiguration(builder.Configuration);
```

### 方式四：AddManInBlack(Action\<ManInBlackOptions\>) `[Obsolete]`

> **已弃用。** 改用 `services.AddManInBlack().AddProvider(...).AddModelChoice(...)` 流式 API。

此方式接受 `Action<ManInBlackOptions>` 委托，内部自动转换为流式 Builder 调用。仍可正常工作，但不推荐新项目使用。

```csharp
// 旧写法（已弃用）
services.AddManInBlack(opt =>
{
    opt.ModelChoice = new ModelChoice
    {
        Schema  = "OpenAI",
        ApiKey  = "sk-xxx",
        BaseUrl = "https://api.deepseek.com",
        ModelId = "deepseek-chat",
    };
});

// 新写法（推荐）
services.AddManInBlack()
    .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx").BaseUrl("https://api.deepseek.com"))
    .AddModelChoice("default", c => c.Provider("default").ModelId("deepseek-chat"));
```

---

## IOptions 访问配置

注册后可通过标准 Options 模式访问：

```csharp
// 启动时快照（IOptions<T>）
public class MyService(IOptions<ManInBlackSettings> options)
{
    var providers = options.Value.Providers;
    var choices = options.Value.ModelChoices;
}

// 跟踪文件变更（IOptionsMonitor<T>）
public class MyService(IOptionsMonitor<ManInBlackSettings> monitor)
{
    var currentChoices = monitor.CurrentValue.ModelChoices;

    monitor.OnChange(settings =>
    {
        // settings.json 变更时触发
    });
}
```

| 接口                    | 生命周期  | 适用场景                         |
| ----------------------- | --------- | -------------------------------- |
| `IOptions<T>`           | Singleton | 只需启动时值                     |
| `IOptionsMonitor<T>`    | Singleton | 需要响应文件变更                 |
| `IOptionsSnapshot<T>`   | Scoped    | 请求内一致，请求间刷新（ASP.NET） |

飞书配置同理：`IOptions<FeishuSettings>` 读取 `Feishu` 子节。

---

## 配置校验

已注册 `IValidateOptions<ManInBlackSettings>` 校验，取值时自动检查：

- `Providers` 至少有一项
- `ModelChoices` 包含 `"default"` 键
- 每个 Provider 的 Schema 为合法值（`"OpenAI"` / `"Anthropic"` / `"Gemini"`）
- 每个 Provider 的 ApiKey 非空
- 每个 ModelChoice 的 ProviderName 在 Providers 中存在
- 每个 Agent 的 PipelineName 非空
- 每个 Agent 的 SubAgents 引用的 Agent 在 Agents 中存在
- Agent 不能将自己列为子 Agent
- Agent 的 ModelChoiceName 若指定，必须在 ModelChoices 中存在

```csharp
// 校验失败时抛 OptionsValidationException
var settings = options.Value;
```

新增校验规则，编辑 `Configuration/ValidateManInBlackSettings.cs`：

```csharp
public ValidateOptionsResult Validate(string? name, ManInBlackSettings options)
{
    if (options.Providers.Count == 0)
        return ValidateOptionsResult.Fail("settings.json 缺少 Providers 配置");

    // ... 其他校验

    return ValidateOptionsResult.Success;
}
```

---

## 沙盒配置

| 字段         | 默认值  | 说明                                                                         |
| ------------ | ------- | ---------------------------------------------------------------------------- |
| `UseSandbox` | `false` | 是否启用 Linux 下的 bubblewrap 沙盒执行命令。仅 Linux 生效，其他平台忽略     |

沙盒通过 `bubblewrap`（bwrap）隔离文件系统和网络，适用于不可信输入场景。启用后 agent 执行的 shell 命令将在沙盒中运行，无法访问宿主文件系统。

```json
{
  "UseSandbox": true
}
```

> **注意：** 容器部署时需确保镜像中安装了 `bubblewrap`。沙盒会隔离文件系统，通过环境变量（如 `GH_TOKEN`）传递凭证的方式不受影响。

---

## ToolExtraParameter 配置

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

---

## 添加新配置字段

1. 在 `ManInBlackSettings` 中添加属性（带合理默认值）：

```csharp
public double Temperature { get; set; } = 1.0;
```

2. 如需校验，在 `ValidateManInBlackSettings` 中添加规则。

完成。老 settings.json 无需迁移——缺少的字段走 C# 默认值，多余的字段自动忽略。

---

## 检查点策略配置

状态持久化（检查点）默认自动启用，无需在 `settings.json` 中额外配置。框架使用 `AfterToolCallPolicy` 作为默认策略，在每轮工具调用后和 session 结束时保存快照。

### 替换检查点策略

通过 DI 注册自定义 `ICheckpointPolicy` 实现来控制保存时机：

```csharp
services.AddSingleton<ICheckpointPolicy, MyCustomPolicy>();

// 示例：仅在 session 结束时保存
public class SessionEndOnlyPolicy : ICheckpointPolicy
{
    public bool ShouldSave(string phase) => phase == "SessionEnd";
}
```

> **注意：** `AfterToolCallPolicy` 通过 `TryAddSingleton` 注册，自定义注册会自动覆盖默认实现。

---

## 配置 API 速查

| API                                              | 用途                                        |
| ------------------------------------------------ | ------------------------------------------- |
| `services.AddManInBlack()`                       | 流式 Builder 入口（推荐）                   |
| `ManInBlackConfigurationBuilder.BuildConfiguration()` | 独立构建 IConfiguration               |
| `IConfigurationBuilder.AddManInBlackSettings()`  | 将配置源加入已有 IConfigurationBuilder       |
| `services.AddManInBlackFromSettings()`           | 便捷注册：从 settings.json 构建配置 + 注册服务（≡ `AddManInBlack().UseJson()`） |
| `services.AddManInBlackFromConfiguration(IConfiguration)` | 从已有 IConfiguration 注册服务（≡ `AddManInBlack().UseConfiguration(cfg)`） |
| `services.AddManInBlack(Action<ManInBlackOptions>)` | [Obsolete] 手动配置，改用流式 API           |

---

## 下一步

- [Provider 配置指南](./provider-guide.md) — 查看所有支持的提供商
- [快速开始](./quick-start.md) — 从零启动一个 Agent
- [架构概览](./architecture.md) — 理解洋葱模型和整体设计
