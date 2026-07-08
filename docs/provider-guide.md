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

## LLM HttpClient

LLM `IChatClient` 走**专属命名 HttpClient** `ManInBlackHttpClients.ChatClient`(`"ManInBlack.Chat"`),在 `AddManInBlack()` 中注册,独立于其他用途(如 GitHub token、飞书 SDK)的 HttpClient。

```csharp
services.AddHttpClient(ManInBlackHttpClients.ChatClient, c => c.Timeout = TimeSpan.FromMinutes(30))
    .RemoveAllResilienceHandlers()   // 移除 host 注入的标准 resilience(默认 30s/次)
    .ConfigurePrimaryHttpMessageHandler(() =>
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
```

设计要点:

- **移除标准 resilience**:Aspire `AddServiceDefaults` 等会经 `ConfigureHttpClientDefaults` 给所有 HttpClient 套上 `AddStandardResilienceHandler`(默认每次尝试 30s 超时 + 3 次重试)。它会砍断推理模型首字节 >30s 的流式请求,且与应用层 `RetryMiddleware` 叠加重复请求。LLM 的重试统一由 `RetryMiddleware` 负责,故主库注册时移除。
- **30 分钟兜底超时**:`HttpClient.Timeout` 覆盖整条流式生命周期(含输出阶段),需远大于 Polly 默认的 30s;正常流式时长由应用层 `CancellationToken` 控制,30 分钟仅防极端静默挂死。
- **OTel 观测不受影响**:`AddHttpClientInstrumentation` hook 的是 HttpClient 传输层(`DiagnosticSource`),与 client 命名/resilience 无关,LLM 请求照常产生 span/metric。span 上不会自动标注 client name;若需在 Dashboard 区分 LLM 调用,可在该命名 client 上额外挂 `DelegatingHandler` 给 `Activity.Current` 打 tag(如 `mib.chat_client = "ManInBlack.Chat"`)。

> `RemoveAllResilienceHandlers` 为评估期 API(`EXTEXP0001`),语义稳定,已在注册处局部 `#pragma` 抑制。

## 按名称获取 ModelChoice

通过 `IOptions<ManInBlackSettings>` 获取非默认的 ModelChoice：

```csharp
var settings = sp.GetRequiredService<IOptions<ManInBlackSettings>>().Value;
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
