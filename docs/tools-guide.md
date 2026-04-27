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

| 方法         | 说明                                     |
| ------------ | ---------------------------------------- |
| `ReadFile`   | 读取文件，支持 offset/length 行范围      |
| `WriteFile`  | 创建/覆盖文件，自动创建父目录            |
| `UpdateFile` | 精确字符串替换，替换前必须先 ReadFile    |
| `Glob`       | 按 glob 模式搜索文件，按修改时间排序     |
| `Grep`       | 按正则搜索文件内容，返回匹配行和行号     |

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
