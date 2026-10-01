# ManInBlack

.NET 10 AI Agent 框架。通过 [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/core/extensions/ai-extensions)（`IChatClient`）统一抽象 OpenAI / Anthropic / Gemini 三种聊天协议，提供洋葱模型中间件管道、源生成器工具派发、MCP 接入与 SQLite 持久化；Linux 下可通过 bubblewrap 沙盒执行命令。

## 安装

```bash
dotnet add package ManInBlack.AI
```

`[AiTool]` / `[SlashCommand]` 源生成器已内嵌在包的 `analyzers/` 目录，装包即用，无需额外安装。

## 快速上手

```csharp
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddManInBlack()
    .UseJson() // 从 ~/.man-in-black/settings.json 读取配置
    .AddAgent("my-agent", a => a
        .Description("通用助手")
        .Instruction("你是一个有帮助的AI助手。请用中文回复。")
        .Pipeline("simple")); // 从最小管道起步，按需开启能力

var rootSp = services.BuildServiceProvider();
var factory = rootSp.GetRequiredService<AgentFactory>();

await foreach (var update in factory.RunAsync(
                   "my-agent", "帮我解释一下什么是依赖注入",
                   parentId: "my-user", parentType: "User"))
{
    Console.Write(update.Text);
}
```

首次运行会在 `~/.man-in-black/settings.json` 自动创建配置模板，填入 API Key 即可：

```json
{
  "Providers": {
    "default": { "Schema": "OpenAI", "ApiKey": "sk-xxxxxxxx" }
  },
  "ModelChoices": {
    "default": { "ProviderName": "default", "ModelId": "gpt-4o" }
  }
}
```

不依赖配置文件、纯代码配置：

```csharp
services.AddManInBlack()
    .AddProvider("default", p => p.Schema("OpenAI").ApiKey("sk-xxx").BaseUrl("https://api.deepseek.com"))
    .AddModelChoice("default", c => c.Provider("default").ModelId("deepseek-chat"))
    .AddAgent("my-agent", a => a.Instruction("你是一个AI助手").Pipeline("simple"));
```

## 自定义工具：标记即注册

工具类标记 `[ServiceRegister.Scoped]`、方法标记 `[AiTool]`（类必须 `partial`），XML 文档注释会成为 LLM 可见的工具描述，源生成器自动完成声明与派发：

```csharp
using ManInBlack.AI.Abstraction.Attributes;

[ServiceRegister.Scoped]
public partial class WeatherTools
{
    /// <summary>查询指定城市的天气。</summary>
    /// <param name="city">城市名，如 "北京"。</param>
    [AiTool]
    public string GetWeather(string city) => /* ... */ $"{city}: 晴 26°C";
}
```

## 包结构

| 包 | 用途 |
| --- | --- |
| `ManInBlack.AI` | 主包：DI 入口、中间件管道、内置工具、MCP、源生成器；默认内存存储 |
| `ManInBlack.AI.Abstraction` | 契约层：Agent 定义、中间件、工具、Hook、存储等抽象（扩展实现方引用） |
| `ManInBlack.AI.Persistence.Sqlite` | 可选包：SQLite 持久化（EF Core 10），安装后覆盖默认内存存储 |
| `ManInBlack.Bwarp` | Linux bubblewrap 沙盒封装（主包的传递依赖，可独立使用） |
| `ManInBlack.AI.SourceGenerator` | 源生成器独立包（已内嵌于主包，一般无需单独安装） |

## 文档

- [快速开始](docs/quick-start.md)
- [架构概览](docs/architecture.md)
- [配置指南](docs/configuration-guide.md) / [Provider 配置指南](docs/provider-guide.md)
- [中间件开发指北](docs/middleware-guide.md)
- [工具开发指北](docs/tools-guide.md)
- [Agent 工厂指南](docs/agent-factory-guide.md)
- [NuGet 打包与发布](docs/nuget-packaging.md)

## 许可证

[MIT](LICENSE)
