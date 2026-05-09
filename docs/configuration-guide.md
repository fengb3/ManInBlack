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

---

## DI 注册方式

### 方式一：AddManInBlackFromSettings（控制台 / 测试）

自动从 `~/.man-in-black/settings.json` 构建 `IConfiguration` 并注册所有服务：

```csharp
services.AddManInBlackFromSettings();
```

内部调用 `ManInBlackConfigurationBuilder.BuildConfiguration()` 构建 `IConfiguration`，启用 `reloadOnChange: true`。

### 方式二：AddManInBlackFromConfiguration（WebApplicationBuilder）

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

### 方式三：AddManInBlack（手动配置）

不读取 settings.json，在代码中直接指定：

```csharp
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
```

此方式不注册 `IOptions<ManInBlackSettings>`，无法使用配置跟踪和校验。

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

## 添加新配置字段

1. 在 `ManInBlackSettings` 中添加属性（带合理默认值）：

```csharp
public double Temperature { get; set; } = 1.0;
```

2. 如需校验，在 `ValidateManInBlackSettings` 中添加规则。

完成。老 settings.json 无需迁移——缺少的字段走 C# 默认值，多余的字段自动忽略。

---

## 配置 API 速查

| API                                              | 用途                                        |
| ------------------------------------------------ | ------------------------------------------- |
| `ManInBlackConfigurationBuilder.BuildConfiguration()` | 独立构建 IConfiguration               |
| `IConfigurationBuilder.AddManInBlackSettings()`  | 将配置源加入已有 IConfigurationBuilder       |
| `services.AddManInBlackFromSettings()`           | 便捷注册：构建配置 + 注册服务                |
| `services.AddManInBlackFromConfiguration(IConfiguration)` | 从已有 IConfiguration 注册服务      |
| `services.AddManInBlack(Action<ManInBlackOptions>)` | 手动配置，不读取 settings.json           |

---

## 下一步

- [Provider 配置指南](./provider-guide.md) — 查看所有支持的提供商
- [快速开始](./quick-start.md) — 从零启动一个 Agent
- [架构概览](./architecture.md) — 理解洋葱模型和整体设计
