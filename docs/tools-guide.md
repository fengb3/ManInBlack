# 工具开发指北

> 本文档是 CLAUDE.md 的子文档，Agent 在修改工具（Tools）、ToolCallFilter 相关代码前应先阅读此文档。

## 架构概览

工具系统由三个核心组件组成：

| 组件 | 职责 | DI 注入 |
|------|------|---------|
| `ToolRegistry` | 声明集中管理 | `IEnumerable<IToolDeclaration>` |
| `ToolExecutor` | 执行集中分发 | `IEnumerable<IToolHandler>` |
| `ToolsMiddleware` | 按 group 选择注入声明到 pipeline | 引用 `ToolRegistry` |

源生成器为每个 `[AiTool]` 方法生成独立的 `IToolHandler` 实现和 `IToolDeclaration` 注册，通过 DI 自动组合跨项目的工具。

## 工具类概览

所有工具类标记 `[ServiceRegister.Scoped]`，方法标记 `[AiTool]`，由源生成器自动生成 handler 和声明注册代码。

### CommandLineTools

| 方法                      | 说明                                        |
| ------------------------- | ------------------------------------------- |
| `RunBash`                 | 执行 bash 命令，支持超时和后台运行           |
| `GetBackgroundTaskResult` | 查询后台任务结果                             |
| `KillBackgroundTask`      | 终止指定后台任务                             |

**安全检查**：`RunBash` 内置危险命令检测（`CheckDangerousCommand`），通过正则匹配拦截递归删除、格式化、fork 炸弹、反向 shell 等操作。

**Bash 选择**：Windows 上优先使用 Git Bash (`ProgramFiles/Git/bin/bash.exe`)，避免 WSL bash。

### FileTools

所有路径支持绝对路径和相对路径（相对于 workspace 根目录）。

**写入权限**：`Write`、`Edit` 允许在工作空间和系统临时目录内操作（Linux/macOS 为 `/tmp`，Windows 为 `%TEMP%`），不允许在其他位置修改、创建或删除文件。

| 方法    | 说明                                                    |
| ------- | ------------------------------------------------------- |
| `Read`  | 读取文件，支持 offset/length 行范围                     |
| `Write` | 创建/覆盖文件，自动创建父目录                           |
| `Edit`  | 精确字符串替换（仅替换首次出现），替换前必须先 `Read`   |
| `Glob`  | 按 glob 模式搜索文件，按修改时间排序                    |
| `Grep`  | 按正则搜索文件内容，返回匹配行和行号                    |

> 注：`DeleteFile` 和 `DeleteDirectory` 已注释掉，暂不可用。

### SkillTools

| 方法         | 说明                       |
| ------------ | -------------------------- |
| `LoadSkill`  | 按名称加载 skill 内容      |

### DelegationTools

| 方法               | 说明                                                         |
| ------------------ | ------------------------------------------------------------ |
| `DelegateToAgent`  | 将任务委托给指定子 Agent 执行，返回子 Agent 的文本输出       |

**参数：**

| 参数        | 类型     | 说明                                         |
| ----------- | -------- | -------------------------------------------- |
| `agentName` | `string` | 要委托的子 Agent 名称（必须在父 Agent 的 SubAgents 列表中） |
| `task`      | `string` | 要委托给子 Agent 的任务描述                   |

**使用方式：** 通过 `DelegationMiddleware` 自动注入，不需要手动注册。只有 `AgentDefinition.SubAgents` 非空的 Agent 才会获得此工具。

---

## ToolCallFilter 管道

每个 `[AiTool]` 方法可通过 `[AiTool.HasFilter<T>]` 声明过滤器。过滤器按洋葱模型执行，包裹在工具调用前后。

| 过滤器               | 作用                                     |
| -------------------- | ---------------------------------------- |
| `LoggingFilter`      | 记录工具名、参数、结果长度到日志         |
| `AgentLifecycleFilter` | 通过 `EventBus` 发布 `BeforeToolExecuteEvent` / `AfterToolExecuteEvent` |

`LargeResultFilter`（已注释）：大结果截断并写入文件，返回截断提示。

---

## 编写自定义工具

1. 创建类，标记 `[ServiceRegister.Scoped]` 和 `partial`
2. 方法标记 `[AiTool]`，参数标记 `[param]` XML 文档
3. 可选：添加 `[AiTool.HasFilter<T>]` 应用过滤器
4. 源生成器自动生成 handler、声明和 DI 注册

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

自定义工具只需定义在引用了 `ManInBlack.AI.SourceGenerator` 的项目中，源生成器会自动生成 handler 和声明注册，与内置工具一起通过 DI 自动组合。

### 复杂对象/数组参数

工具参数可使用对象或集合，源生成器自动生成嵌套 JSON Schema 并在运行时反序列化：

```csharp
public class ChoiceOption
{
    /// <summary>选项文案</summary>
    public string Label { get; set; } = "";
    /// <summary>选项说明（可选）</summary>
    public string? Description { get; set; }
}

[AiTool]
public string Ask(string question, List<ChoiceOption> options) => ...;
```

> 对象成员取**公共可读属性**（schema 属性名转 camelCase）；运行时反序列化大小写不敏感，enum 既接受名称也接受数字。
> `Dictionary<,>`、元组、开放泛型、`object` 等参数类型不受支持，会触发 MIB014 编译错误。

### 在自定义 Pipeline 中使用工具

```csharp
// 注入所有工具
builder.Use<ToolsMiddleware>()

// 按组选择工具（Group 为工具类短名）
builder.Use(sp => new ToolsMiddleware(
    sp.GetRequiredService<ToolRegistry>(),
    ["FileTools", "MyTools"]))
```

详见 [Source Generator & 诊断规则](./sourcegenerator-guide.md) 了解源生成器和 XML 文档要求。

---

## AskUser（仅 FeishuAdaptor）

向当前飞书用户发送一张单选/多选卡片，**阻塞等待**用户在飞书里点选，把选择结果作为工具返回值交回 LLM。适合需要用户拍板的场景（确认、分支选择、多选收集）。

```csharp
[AiTool]
public async Task<string> AskUserAsync(
    string question,                 // 问题文本
    List<AskUserOption> options,     // 可选项：Label 必填，Description/Value 可选
    bool multiSelect = false,        // true=多选下拉+提交；false=按钮单选（点一下即返回）
    int timeoutSeconds = 300);       // 超时自动结束
```

- 单选：每个选项一张按钮，点任一按钮立即返回 `用户选择了：{label}`。
- 多选：飞书原生 `multi_select_static` + 提交按钮，点提交后返回 `用户选择了：{l1}、{l2}`。
- 超时返回 `用户未在 N 秒内作答（已超时）`；会话被取消返回 `提问已被取消…`。

**部署前置**：飞书应用后台须订阅「卡片回传交互」事件并走 webhook，按钮点击回调才能送达（否则工具必超时）。回调由 `CardActionCallbackHandler`（FeishuNetSdk `ICallbackHandler`）接收，自动发现，无需手动注册。
