# Provider 配置指南

本文档列出 ManInBlack 支持的所有 AI 提供商及其配置方式。

---

## 概览

15 个提供商通过 `CompatibleWith` 字段映射到 3 种 API 协议：

| 协议      | 适配器                          | 认证方式                                 |
| --------- | ------------------------------- | ---------------------------------------- |
| OpenAI    | `OpenAICompatibleChatClient`    | `Authorization: Bearer {key}`            |
| Anthropic | `AnthropicCompatibleChatClient` | `x-api-key: {key}` + `anthropic-version` |
| Gemini    | `GeminiCompatibleChatClient`    | URL Query `?key={key}`                   |

绝大多数中国厂商兼容 OpenAI 协议，无需额外适配。

---

## 完整提供商列表

### OpenAI 协议兼容

这些提供商使用标准的 OpenAI Chat Completions API。

| 类名                      | 提供商       | 默认 BaseUrl                                     | settings.json 中的 Provider 值 |
| ------------------------- | ------------ | ------------------------------------------------ | ------------------------------ |
| `OpenAIProvider`          | OpenAI       | `https://api.openai.com`                         | `"OpenAI"`                     |
| `DeepSeekProvider`        | DeepSeek     | `https://api.deepseek.com`                       | `"DeepSeek"`                   |
| `KimiCNProvider`          | Kimi (国内)  | `https://api.moonshot.cn`                        | `"KimiCN"` 或 `"Kimi-cn"`      |
| `KimiAIProvider`          | Kimi (国际)  | `https://api.moonshot.ai`                        | `"KimiAI"` 或 `"Kimi-ai"`      |
| `QwenProvider`            | 通义千问     | `https://dashscope.aliyuncs.com/compatible-mode` | `"Qwen"`                       |
| `ZhipuProvider`           | 智谱 AI      | `https://open.bigmodel.cn/api/paas/v4`           | `"Zhipu"`                      |
| `ZhipuCodingPlanProvider` | 智谱编程计划 | `https://open.bigmodel.cn/api/coding/paas/v4`    | `"ZhipuCodingPlan"`            |
| `YiProvider`              | 零一万物     | `https://api.lingyiwanwu.com`                    | `"Yi"`                         |
| `BaichuanProvider`        | 百川智能     | `https://api.baichuan-ai.com`                    | `"Baichuan"`                   |
| `StepFunProvider`         | 阶跃星辰     | `https://api.stepfun.com`                        | `"StepFun"`                    |
| `SparkProvider`           | 讯飞星火     | `https://spark-api-open.xf-yun.com`              | `"Spark"`                      |
| `DoubaoProvider`          | 豆包 (字节)  | `https://ark.cn-beijing.volces.com/api`          | `"Doubao"`                     |
| `MiniMaxProvider`         | MiniMax      | `https://api.minimax.chat`                       | `"MiniMax"`                    |

### Anthropic 协议兼容

| 类名                | 提供商    | 默认 BaseUrl                | Provider 值   |
| ------------------- | --------- | --------------------------- | ------------- |
| `AnthropicProvider` | Anthropic | `https://api.anthropic.com` | `"Anthropic"` |

### Gemini 协议兼容

| 类名             | 提供商 | 默认 BaseUrl                                | Provider 值 |
| ---------------- | ------ | ------------------------------------------- | ----------- |
| `GeminiProvider` | Google | `https://generativelanguage.googleapis.com` | `"Gemini"`  |

---

## 配置方式

### 方式一：settings.json（推荐）

在 `~/.man-in-black/settings.json` 中配置：

```json
{
  "Provider": "DeepSeek",
  "ApiKey": "sk-xxxxxxxx",
  "BaseUrl": "https://api.deepseek.com",
  "ModelId": "deepseek-chat"
}
```

`BaseUrl` 可选，每个 Provider 有默认值。使用 `AddManInBlackFromSettings()` 加载：

```csharp
services.AddManInBlackFromSettings();
```

支持文件变更跟踪和 `IOptionsMonitor` 访问，详见 [配置指南](./configuration-guide.md)。

### 方式二：代码配置

直接在代码中创建 Provider 实例：

```csharp
services.AddManInBlack(opt =>
{
    opt.ModelChoice = new ModelChoice
    {
        Provider = new DeepSeekProvider()
        {
            ApiKey = "sk-xxx",
            BaseUrl = "https://api.deepseek.com",
        },
        ModelId = "deepseek-chat",
    };
});
```

所有 Provider 类在 `ManInBlack.AI` 命名空间下。

### 方式三：使用代理 / 中转 API

将 `BaseUrl` 指向你的代理地址：

```json
{
  "Provider": "OpenAI",
  "ApiKey": "your-key",
  "BaseUrl": "https://proxy.example.com/v1",
  "ModelId": "gpt-4o"
}
```

---

## 各厂商常用模型 ID

### OpenAI

| 模型 ID       | 说明       |
| ------------- | ---------- |
| `gpt-4o`      | 旗舰多模态 |
| `gpt-4o-mini` | 轻量快速   |
| `o4-mini`     | 推理模型   |
| `gpt-4.1`     | 最新 GPT-4 |

### Anthropic

| 模型 ID                     | 说明          |
| --------------------------- | ------------- |
| `claude-sonnet-4-6`         | Claude Sonnet |
| `claude-opus-4-6`           | Claude Opus   |
| `claude-haiku-4-5-20251001` | Claude Haiku  |

### DeepSeek

| 模型 ID             | 说明        |
| ------------------- | ----------- |
| `deepseek-chat`     | DeepSeek-V3 |
| `deepseek-reasoner` | DeepSeek-R1 |

### 通义千问 (Qwen)

| 模型 ID      | 说明 |
| ------------ | ---- |
| `qwen-max`   | 旗舰 |
| `qwen-plus`  | 均衡 |
| `qwen-turbo` | 快速 |

### 智谱 (Zhipu)

| 模型 ID       | 说明  |
| ------------- | ----- |
| `glm-4-plus`  | GLM-4 |
| `glm-4-flash` | 轻量  |

---

## 注册新提供商

只需创建类继承 `ModelProvider`（定义在 `ManInBlack.AI.Abstraction`）：

```csharp
using ManInBlack.AI.Abstraction;

namespace ManInBlack.AI;

public sealed class MyProvider : ModelProvider
{
    public override string ProviderName  => "MyProvider";
    public override string BaseUrl       { get; set; } = "https://api.example.com";
    public override string CompatibleWith => "OpenAI";  // 或 "Anthropic" / "Gemini"
}
```

无需编写适配器代码 —— 只要 `CompatibleWith` 指向现有协议，工厂方法会自动创建对应的 `IChatClient` 实例。

如果希望通过 `settings.json` 配置，还需在 `SettingsLoader.CreateProvider()` 中添加映射：

```csharp
"MyProvider" => new MyProvider(),
```

---

## 运行时提供商切换

通过 `ModelChoice` 在运行时动态选择：

```csharp
// 不同任务用不同模型
var chatChoice = new ModelChoice
{
    Provider = new DeepSeekProvider() { ApiKey = deepseekKey },
    ModelId  = "deepseek-chat",
};

var codeChoice = new ModelChoice
{
    Provider = new OpenAIProvider() { ApiKey = openaiKey },
    ModelId  = "gpt-4o",
};
```

每次构建管道时可以传入不同的 `ModelChoice`，由 DI 容器中的 `IChatClient` 单例策略决定实际模型工厂逻辑。
