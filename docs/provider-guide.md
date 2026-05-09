# Provider 配置指南

本文档说明 ManInBlack 的 AI 提供商配置方式。

---

## 概览

ManInBlack 支持三种 API 协议（Schema），通过 `Schema` 字段指定：

| Schema      | 适配器                          | 认证方式                                 | 默认 BaseUrl                                    |
| ----------- | ------------------------------- | ---------------------------------------- | ----------------------------------------------- |
| `OpenAI`    | `OpenAICompatibleChatClient`    | `Authorization: Bearer {key}`            | `https://api.openai.com`                        |
| `Anthropic` | `AnthropicCompatibleChatClient` | `x-api-key: {key}` + `anthropic-version` | `https://api.anthropic.com`                     |
| `Gemini`    | `GeminiCompatibleChatClient`    | URL Query `?key={key}`                   | `https://generativelanguage.googleapis.com`     |

绝大多数厂商（DeepSeek、通义千问、智谱、Kimi、豆包等）兼容 OpenAI 协议，只需更改 `BaseUrl` 即可接入。

---

## 配置方式

### 方式一：settings.json（推荐）

在 `~/.man-in-black/settings.json` 中配置：

```json
{
  "Providers": {
    "openai": {
      "Schema": "OpenAI",
      "ApiKey": "sk-xxx",
      "BaseUrl": "https://api.openai.com"
    },
    "deepseek": {
      "Schema": "OpenAI",
      "ApiKey": "sk-yyy",
      "BaseUrl": "https://api.deepseek.com"
    },
    "claude": {
      "Schema": "Anthropic",
      "ApiKey": "sk-zzz"
    }
  },
  "ModelChoices": {
    "default": {
      "ProviderName": "openai",
      "ModelId": "gpt-4o"
    },
    "deepseek-chat": {
      "ProviderName": "deepseek",
      "ModelId": "deepseek-chat"
    },
    "claude-sonnet": {
      "ProviderName": "claude",
      "ModelId": "claude-sonnet-4-20250514"
    }
  }
}
```

**结构说明：**

- **Providers**：字典，key 为自定义名称。每个 provider 包含：
  - `Schema`（必填）：协议类型，只允许 `"OpenAI"` / `"Anthropic"` / `"Gemini"`
  - `ApiKey`（必填）：API 密钥
  - `BaseUrl`（可选）：API 基础地址，不填则使用 Schema 对应的默认值
- **ModelChoices**：字典，key 为自定义名称，**必须包含 `"default"`**。每个 choice 包含：
  - `ProviderName`（必填）：引用 Providers 中的 key
  - `ModelId`（必填）：模型标识符

使用 `AddManInBlackFromSettings()` 加载，默认使用 `ModelChoices["default"]`：

```csharp
services.AddManInBlackFromSettings();
```

支持文件变更跟踪和 `IOptionsMonitor` 访问，详见 [配置指南](./configuration-guide.md)。

### 方式二：代码配置

直接在代码中创建 `ModelChoice`：

```csharp
services.AddManInBlack(opt =>
{
    opt.ModelChoice = new ModelChoice
    {
        Schema = "OpenAI",
        ApiKey = "sk-xxx",
        BaseUrl = "https://api.deepseek.com",
        ModelId = "deepseek-chat",
    };
});
```

### 方式三：使用代理 / 中转 API

将 `BaseUrl` 指向你的代理地址：

```json
{
  "Providers": {
    "proxy": {
      "Schema": "OpenAI",
      "ApiKey": "your-key",
      "BaseUrl": "https://proxy.example.com/v1"
    }
  },
  "ModelChoices": {
    "default": {
      "ProviderName": "proxy",
      "ModelId": "gpt-4o"
    }
  }
}
```

---

## 按名称获取 ModelChoice

通过 `ManInBlackSettings` 获取非默认的 ModelChoice：

```csharp
var settings = sp.GetRequiredService<ManInBlackSettings>();
var choice = settings.GetModelChoice("deepseek-chat");
var chatClient = ChatClientProviderExtensions.CreateChatClient(
    sp.GetRequiredService<IHttpClientFactory>(), choice);
```

---

## 常见厂商配置示例

### DeepSeek

```json
{
  "Schema": "OpenAI",
  "ApiKey": "sk-xxx",
  "BaseUrl": "https://api.deepseek.com"
}
```

### 通义千问（Qwen）

```json
{
  "Schema": "OpenAI",
  "ApiKey": "sk-xxx",
  "BaseUrl": "https://dashscope.aliyuncs.com/compatible-mode"
}
```

### 智谱 AI

```json
{
  "Schema": "OpenAI",
  "ApiKey": "xxx.xxx",
  "BaseUrl": "https://open.bigmodel.cn/api/paas/v4"
}
```

### Google Gemini

```json
{
  "Schema": "Gemini",
  "ApiKey": "xxx"
}
```
