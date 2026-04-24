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

| 类名                      | 提供商       | 默认 BaseUrl                                     |
| ------------------------- | ------------ | ------------------------------------------------ |
| `OpenAIProvider`          | OpenAI       | `https://api.openai.com`                         |
| `DeepSeekProvider`        | DeepSeek     | `https://api.deepseek.com`                       |
| `KimiCNProvider`          | Kimi (国内)  | `https://api.moonshot.cn`                        |
| `KimiAIProvider`          | Kimi (国际)  | `https://api.moonshot.ai`                        |
| `QwenProvider`            | 通义千问     | `https://dashscope.aliyuncs.com/compatible-mode` |
| `ZhipuProvider`           | 智谱 AI      | `https://open.bigmodel.cn/api/paas/v4`           |
| `ZhipuCodingPlanProvider` | 智谱编程计划 | `https://open.bigmodel.cn/api/coding/paas/v4`    |
| `YiProvider`              | 零一万物     | `https://api.lingyiwanwu.com`                    |
| `BaichuanProvider`        | 百川智能     | `https://api.baichuan-ai.com`                    |
| `StepFunProvider`         | 阶跃星辰     | `https://api.stepfun.com`                        |
| `SparkProvider`           | 讯飞星火     | `https://spark-api-open.xf-yun.com`              |
| `DoubaoProvider`          | 豆包 (字节)  | `https://ark.cn-beijing.volces.com/api`          |
| `MiniMaxProvider`         | MiniMax      | `https://api.minimax.chat`                       |

### Anthropic 协议兼容

| 类名                | 提供商    | 默认 BaseUrl                |
| ------------------- | --------- | --------------------------- |
| `AnthropicProvider` | Anthropic | `https://api.anthropic.com` |

### Gemini 协议兼容

| 类名             | 提供商 | 默认 BaseUrl                                |
| ---------------- | ------ | ------------------------------------------- |
| `GeminiProvider` | Google | `https://generativelanguage.googleapis.com` |

---

## 配置方式

### 方式一：直接初始化

```csharp
var provider = new OpenAIProvider()
{
    ApiKey  = "sk-xxxxxxxx",
    BaseUrl = "https://api.openai.com",  // 可选，有默认值
};

var modelChoice = new ModelChoice
{
    Provider = provider,
    ModelId  = "gpt-4o",
};
```

### 方式二：通过 .env 文件（推荐）

```csharp
// 读取环境变量
Env.Load();

var modelChoice = new ModelChoice
{
    Provider = new DeepSeekProvider()
    {
        ApiKey  = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "",
        BaseUrl = Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL")
                  ?? "https://api.deepseek.com",
    },
    ModelId = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL_ID") ?? "deepseek-chat",
};
```

### 方式三：使用代理 / 中转 API

将 `BaseUrl` 指向你的代理地址：

```csharp
var provider = new OpenAIProvider()
{
    ApiKey  = "your-key",
    BaseUrl = "https://proxy.example.com/v1",  // 自定义代理地址
};
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

只需创建类继承 `ModelProvider`：

```csharp
public sealed class MyProvider : ModelProvider
{
    public override string ProviderName  => "MyProvider";
    public override string BaseUrl       { get; set; } = "https://api.example.com";
    public override string CompatibleWith => "OpenAI";  // 或 "Anthropic" / "Gemini"
}
```

无需编写适配器代码 —— 只要 `CompatibleWith` 指向现有协议，工厂方法会自动创建对应的 `IChatClient` 实例。

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
