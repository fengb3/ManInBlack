# 工具开发指北

> 本文档是 CLAUDE.md 的子文档，Agent 在修改工具（Tools）、ToolCallFilter 相关代码前应先阅读此文档。

## 工具类概览

所有工具类标记 `[ServiceRegister.Scoped]`，方法标记 `[AiTool]`，由源生成器自动生成声明和分发代码。

### CommandLineTools

| 方法                    | 说明                                        |
| ----------------------- | ------------------------------------------- |
| `RunBash`               | 执行 bash 命令，支持超时和后台运行           |
| `GetBackgroundTaskResult` | 查询后台任务结果                           |

**安全检查**：`RunBash` 内置危险命令检测（`CheckDangerousCommand`），通过正则匹配拦截递归删除、格式化、fork 炸弹、反向 shell 等操作。

**Bash 选择**：Windows 上优先使用 Git Bash (`ProgramFiles/Git/bin/bash.exe`)，避免 WSL bash。

### FileTools

所有路径支持绝对路径和相对路径（相对于 workspace 根目录）。

**写入权限**：`WriteFile`、`UpdateFile`、`DeleteFile`、`DeleteDirectory` 允许在工作空间和系统临时目录内操作（Linux/macOS 为 `/tmp`，Windows 为 `%TEMP%`），不允许在其他位置修改、创建或删除文件。

| 方法              | 说明                                              |
| ----------------- | ------------------------------------------------- |
| `ReadFile`        | 读取文件，支持 offset/length 行范围               |
| `WriteFile`       | 创建/覆盖文件，自动创建父目录                     |
| `UpdateFile`      | 精确字符串替换，替换前必须先 ReadFile             |
| `Glob`            | 按 glob 模式搜索文件，按修改时间排序              |
| `Grep`            | 按正则搜索文件内容，返回匹配行和行号              |
| `DeleteFile`      | 删除指定文件                                      |
| `DeleteDirectory` | 递归删除指定目录及其所有内容                      |

### SkillTools

| 方法         | 说明                       |
| ------------ | -------------------------- |
| `LoadSkill`  | 按名称加载 skill 内容      |

---

## ToolCallFilter 管道

每个 `[AiTool]` 方法可通过 `[AiTool.HasFilter<T>]` 声明过滤器。过滤器按洋葱模型执行，包裹在工具调用前后。

| 过滤器               | 作用                                     |
| -------------------- | ---------------------------------------- |
| `LoggingFilter`      | 记录工具名、参数、结果长度到日志         |
| `BroadCastingFilter` | 通过 `EventBus` 发布 `ToolExecutingEvent` / `ToolExecutedEvent` |

`LargeResultFilter`（已注释）：大结果截断并写入文件，返回截断提示。

---

## 编写自定义工具

1. 创建类，标记 `[ServiceRegister.Scoped]` 和 `partial`
2. 方法标记 `[AiTool]`，参数标记 `[param]` XML 文档
3. 可选：添加 `[AiTool.HasFilter<T>]` 应用过滤器
4. 源生成器自动处理声明生成和调用分发

```csharp
[ServiceRegister.Scoped]
public partial class MyTools
{
    /// <summary>
    /// 工具描述
    /// </summary>
    /// <param name="input">参数描述</param>
    /// <returns>返回值描述</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter>]
    public string MyMethod(string input) => $"Result: {input}";
}
```

详见 [Source Generator & 诊断规则](./sourcegenerator-guide.md) 了解源生成器和 XML 文档要求。

---

## 动态提示词覆盖

ToolPromptMiddleware 可在运行时动态覆盖工具的描述、参数描述、返回值描述，以及动态增加参数。覆盖不影响原始声明（创建新实例替换）。

### 配置文件方式

通过 `settings.json` 配置工具提示词覆盖：

```json
{
  "ToolDescriptions": [
    {
      "ToolName": "MyTools.MyMethod",
      "Description": "新的工具描述",
      "ParameterOverrides": {
        "input": "新的参数描述"
      },
      "ReturnsDescription": "新的返回值描述",
      "AdditionalParameters": [
        {
          "Name": "newParam",
          "Type": "int",
          "Description": "动态新增的参数",
          "Required": false
        }
      ]
    }
  ]
}
```

配置通过 `IOptionsMonitor<ManInBlackSettings>` 支持热更新，无需重启即可生效。

### Per-request 方式

通过 `AgentContext.ToolDescriptionOverrides` 属性实现请求级别的覆盖：

```csharp
// 在 Agent 初始化或处理请求时
agentContext.ToolDescriptionOverrides = new List<ToolDescriptionOverride>
{
    new ToolDescriptionOverride
    {
        ToolName = "MyTools.MyMethod",
        Description = "临时覆盖的工具描述",
        ParameterOverrides = new Dictionary<string, string>
        {
            { "input", "临时覆盖的参数描述" }
        },
        AdditionalParameters = new List<ToolParameterOverride>
        {
            new ToolParameterOverride
            {
                Name = "tempParam",
                Type = "string",
                Description = "临时参数"
            }
        }
    }
};
```

### 优先级规则

工具描述解析按以下优先级执行（从高到低）：

1. **Per-request 覆盖**：`AgentContext.ToolDescriptionOverrides`
2. **配置文件覆盖**：`IOptionsMonitor<ManInBlackSettings>.CurrentValue.ToolDescriptions`
3. **原始 XML Doc**：`[AiTool]` 方法的 XML 文档注释

### API 模型

#### ToolDescriptionOverride

| 属性 | 类型 | 说明 | 必需 |
|------|------|------|------|
| `ToolName` | string | 工具全名（格式：类名.方法名） | 是 |
| `Description` | string? | 新的工具描述 | 否 |
| `ParameterOverrides` | Dictionary<string, string>? | 参数描述覆盖 | 否 |
| `ReturnsDescription` | string? | 新的返回值描述 | 否 |
| `AdditionalParameters` | List<ToolParameterOverride>? | 动态新增参数 | 否 |

#### ToolParameterOverride

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Name` | string | - | 参数名 |
| `Type` | string | "string" | 参数类型 |
| `Description` | string? | - | 参数描述 |
| `Required` | bool | false | 是否必需 |
| `IsNullable` | bool | false | 是否可为 null |

### 动态新增参数

新增的参数默认为可选（不在 `required` 数组中），LLM 决定是否提供该参数的值。LLM 传入的动态参数值会包含在 `ToolExecuteContext.Arguments` 字典中，可在工具过滤器的 `OnToolExecutingAsync` 中读取：

```csharp
// 在 ToolCallFilter 中读取动态参数
public async Task OnToolExecutingAsync(ToolExecuteContext context)
{
    if (context.Arguments?.TryGetValue("unit", out var unit) == true)
    {
        var unitStr = unit?.ToString();
        // 使用动态参数值...
    }
}
```
