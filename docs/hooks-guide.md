# Hook 开发指北

> 本文档是 CLAUDE.md 的子文档，Agent 在修改 Hook 相关代码前应先阅读此文档。

## 概述

Hook 系统允许用户通过外部脚本在 Agent 执行生命周期的关键节点注入自定义逻辑，例如安全检查、上下文注入、审计日志等，无需修改框架代码。

---

## 核心概念

Hook 系统基于 **挂载点（HookPoint）** 和 **脚本合约（HookContext / HookResult）** 两个核心抽象：

- **挂载点**：定义在 `HookPoint` 枚举中，对应 Agent 执行生命周期的 6 个具体节点
- **脚本合约**：`HookContext` 作为输入（stdin JSON），`HookResult` 作为输出（stdout JSON）

Hook 通过两个集成层接入框架：

| 集成层    | 类                     | 触发的挂载点                                 |
|--------|-----------------------|----------------------------------------|
| 中间件层   | `HookMiddleware`      | `BeforeLlmCall`、`AgentCompleted`       |
| 工具过滤器层 | `AgentLifecycleFilter` | `BeforeToolExecute`、`AfterToolExecute` |
| 循环中间件层 | `AgentLoopMiddleware` | `AfterLlmCall`、`AllToolsCompleted`     |

---

## 挂载点一览

> **注意：** Hook 通过 EventBus 间接触发。`AgentLoopMiddleware` 发布 `AfterLlmCallEvent` 和 `AllToolsCompletedEvent`，`HookMiddleware` 订阅这些事件并执行对应的 Hook 脚本。`BeforeLlmCall` 和 `AgentCompleted` 由 `HookMiddleware` 直接发布；`BeforeToolExecute` 和 `AfterToolExecute` 由 `AgentLifecycleFilter` 发布和订阅。

| 挂载点                 | 触发时机                           | 实现者                   | 可执行的操作                |
|---------------------|--------------------------------|-----------------------|-----------------------|
| `BeforeLlmCall`     | 首次 LLM 调用前                     | `HookMiddleware`      | 注入 SystemPrompt、修改上下文 |
| `AfterLlmCall`      | LLM 响应流结束后                     | `AgentLoopMiddleware` | 检查响应内容、记录日志           |
| `BeforeToolExecute` | 单个工具执行前                        | `AgentLifecycleFilter` | 阻断执行、检查参数             |
| `AfterToolExecute`  | 单个工具执行后                        | `AgentLifecycleFilter` | 检查结果、审计日志             |
| `AllToolsCompleted` | 本批次所有工具执行完毕后（工具执行被取消/打断时不触发） | `AgentLoopMiddleware` | 批量后处理                 |
| `AgentCompleted`    | Agent 循环结束（无更多 function call）时 | `HookMiddleware`      | 最终处理、清理资源             |

---

## 配置方式

Hook 配置分为全局配置和用户级配置两个层级。

### 全局配置

在 `settings.json` 的 `Hooks` 字段中定义，对所有工作空间生效：

```json
{
  "Providers": {
    "default": {
      "Schema": "OpenAI",
      "ApiKey": "sk-xxx"
    }
  },
  "ModelChoices": {
    "default": {
      "ProviderName": "default",
      "ModelId": "gpt-4o"
    }
  },
  "Hooks": [
    {
      "Name": "安全检查",
      "HookPoint": "BeforeToolExecute",
      "Script": "python security_check.py",
      "ToolNames": [
        "RunBash"
      ],
      "TimeoutMs": 5000,
      "Enabled": true
    }
  ]
}
```

### 用户级配置

在工作空间目录下的 `{workspace}/.agents/mib-hooks.json` 中定义，仅对当前工作空间生效：

```json
[
  {
    "Name": "上下文注入",
    "HookPoint": "BeforeLlmCall",
    "Script": "python inject_context.py",
    "ToolNames": [],
    "TimeoutMs": 10000,
    "Enabled": true
  },
  {
    "Name": "审计日志",
    "HookPoint": "AfterToolExecute",
    "Script": "python audit_log.py",
    "ToolNames": [],
    "TimeoutMs": 10000,
    "Enabled": true
  }
]
```

### 字段说明

| 字段          | 类型         | 说明                                                                |
|-------------|------------|-------------------------------------------------------------------|
| `Name`      | `string`   | 钩子名称，用于日志和调试                                                      |
| `HookPoint` | `string`   | 挂载点名称，对应 `HookPoint` 枚举值                                          |
| `Script`    | `string`   | 脚本命令，需包含解释器前缀（如 `python script.py`）                               |
| `ToolNames` | `string[]` | 仅对指定工具名生效（仅 `BeforeToolExecute` / `AfterToolExecute` 有效），为空表示所有工具 |
| `TimeoutMs` | `int`      | 脚本执行超时时间（毫秒），默认 10000                                             |
| `Enabled`   | `bool`     | 是否启用，默认 true                                                      |

---

## 目录结构

```
# 全局脚本目录
{RootPath}/hooks/
  └── security_check.py      # 全局钩子脚本，Script 字段中的路径相对于此目录

# 用户脚本目录
{workspace}/
  └── .agents/
      ├── mib-hooks.json      # 用户钩子配置
      └── hooks/
          └── audit_log.py    # 用户钩子脚本，工作目录为 {workspace}
```

- 全局钩子脚本的工作目录为 `{RootPath}/hooks/`
- 用户钩子脚本的工作目录为 `{workspace}/`

---

## 脚本合约

### HookContext 输入

框架将 `HookContext` 序列化为 JSON 写入脚本的标准输入（stdin），写完即关闭 stdin 发送 EOF；脚本从 stdin 读取完整 JSON（读到 EOF 即为完整输入）。

**通用字段**：

| 字段          | 类型       | 说明          |
|-------------|----------|-------------|
| `HookPoint` | `string` | 触发此钩子的挂载点名称 |
| `AgentId`   | `string` | Agent 实例标识  |

**按挂载点分组的可用字段**：

| 字段              | `BeforeLlmCall` | `AfterLlmCall` | `BeforeToolExecute` | `AfterToolExecute` | `AllToolsCompleted` | `AgentCompleted` |
|-----------------|-----------------|----------------|---------------------|--------------------|---------------------|------------------|
| `SystemPrompt`  | 可用              | 可用             | -                   | -                  | -                   | 可用               |
| `UserInput`     | 可用              | 可用             | -                   | -                  | -                   | -                |
| `ToolName`      | -               | -              | 可用                  | 可用                 | -                   | -                |
| `CallId`        | -               | -              | 可用                  | 可用                 | -                   | -                |
| `ArgumentsJson` | -               | -              | 可用                  | 可用                 | -                   | -                |
| `ResultJson`    | -               | -              | -                   | 可用                 | -                   | -                |
| `Error`         | -               | -              | -                   | 可用                 | -                   | -                |

### HookResult 输出

脚本将 `HookResult` 以 JSON 格式输出到 stdout。如果 stdout 为空或空白，视为无操作（no-op）。

| 字段             | 类型        | 说明                                                       |
|----------------|-----------|----------------------------------------------------------|
| `IsBlocked`    | `bool`    | 是否阻断执行（仅 `BeforeToolExecute` 有效）                         |
| `BlockReason`  | `string?` | 阻断原因，会作为 `FunctionResultContent` 返回给模型                   |
| `InjectedText` | `string?` | 注入到上下文的额外文本                                              |
| `InjectTarget` | `string?` | 注入目标：`"SystemPrompt"` / `"UserMessage"` / `"ToolResult"` |
| `Succeeded`    | `bool`    | 脚本是否成功执行（false 表示脚本本身出错），默认 true                         |
| `ErrorMessage` | `string?` | 脚本错误信息（`Succeeded=false` 时）                              |

### Script 字段说明

`Script` 字段是原始 shell 命令，不是文件路径。必须包含解释器前缀：

```json
// 正确
"Script": "python security_check.py"
"Script": "node audit.js"

// 错误（缺少解释器，无法直接执行）
"Script": "security_check.py"
```

---

## 执行顺序

1. **全局先于用户**：全局钩子（来自 `settings.json`）先执行，用户钩子（来自 `mib-hooks.json`）后执行
2. **同 HookPoint 按配置顺序**：同一挂载点的多个钩子按配置数组中的顺序依次执行
3. **IsBlocked 短路**：第一个返回 `IsBlocked=true` 的钩子会中断后续钩子的执行
4. **InjectedText 拼接**：多个钩子的 `InjectedText` 会被拼接为单个字符串（用换行符分隔）

---

## 安全机制

| 机制         | 说明                                                    |
|------------|-------------------------------------------------------|
| **超时**     | 每个脚本有独立的 `TimeoutMs`（默认 10 秒），超时后强制终止                 |
| **容错**     | 脚本异常不会向上传播，只会记录警告日志并返回 `Succeeded=false`              |
| **零开销**    | 没有配置任何钩子时，`HookExecutor.ExecuteAsync` 直接返回空结果，不执行任何脚本 |
| **stdin 传参** | HookContext JSON 经 stdin 一次性传入脚本，写入后关闭 stdin（发 EOF）；不落临时文件，无磁盘残留           |

---

## 示例脚本

### BeforeToolExecute 安全检查

阻断危险的 bash 命令：

```python
# security_check.py
import json
import sys


def main():
    ctx = json.load(sys.stdin)

    tool_name = ctx.get("ToolName", "")
    arguments = ctx.get("ArgumentsJson", "")

    if tool_name == "RunBash":
        dangerous = ["rm -rf", "mkfs", "dd if=", ":(){ :|:& };:"]
        for cmd in dangerous:
            if cmd in arguments:
                print(json.dumps({
                    "IsBlocked": True,
                    "BlockReason": f"安全检查拦截：检测到危险命令模式 '{cmd}'"
                }))
                return

    # 不输出任何内容 = no-op
    pass


if __name__ == "__main__":
    main()
```

### BeforeLlmCall 上下文注入

向 SystemPrompt 追加额外指令：

```python
# inject_context.py
import json
import sys


def main():
    ctx = json.load(sys.stdin)

    # 追加项目规范到 SystemPrompt
    extra = "当前项目使用 file-scoped namespace 和 nullable enabled，注释使用中文。"
    print(json.dumps({
        "InjectedText": extra,
        "InjectTarget": "SystemPrompt"
    }))


if __name__ == "__main__":
    main()
```

### AfterToolExecute 审计日志

记录所有工具调用的执行结果：

```python
# audit_log.py
import json
import sys
from datetime import datetime


def main():
    ctx = json.load(sys.stdin)

    log_entry = {
        "timestamp": datetime.now().isoformat(),
        "tool": ctx.get("ToolName"),
        "call_id": ctx.get("CallId"),
        "has_error": ctx.get("Error") is not None,
        "error": ctx.get("Error"),
    }

    with open("tool_audit.jsonl", "a", encoding="utf-8") as f:
        f.write(json.dumps(log_entry, ensure_ascii=False) + "\n")

    # 不需要返回任何内容


if __name__ == "__main__":
    main()
```

---

## 管道位置

### AgentMiddleware 管道

`HookMiddleware` 包裹在 `AgentLoopMiddleware` 外层，形成洋葱模型：

```
ReadPersistenceMiddleware
  └─ SavePersistenceMiddleware
      └─ SkillMiddleware
          └─ AgentProfileMiddleware
              └─ ContextCompressMiddleware
                  └─ CommandLineToolsMiddleware
                      └─ FileToolsMiddleware
                          └─ LoggingMiddleware
                              └─ MessageEnrichMiddleware
                                  └─ HookMiddleware          ← BeforeLlmCall → AgentCompleted
                                      └─ SystemPromptInjectionMiddleware
                                          └─ UserInputMiddleware
                                              └─ RetryMiddleware
                                                  └─ AgentLoopMiddleware  ← AfterLlmCall → AllToolsCompleted
```

- `HookMiddleware` 在 `next()` 前触发 `BeforeLlmCall`，在 `next()` 返回后（无 function call 时）触发 `AgentCompleted`
- `AgentLoopMiddleware` 在每次 LLM 响应流结束后触发 `AfterLlmCall`，在每批工具执行完毕后触发 `AllToolsCompleted`

### ToolCallFilter 管道

`AgentLifecycleFilter` 包裹在每个工具调用前后：

```
AgentLifecycleFilter           ← BeforeToolExecute → AfterToolExecute
  └─ LoggingFilter
      └─ 实际工具调用
```

- `AgentLifecycleFilter` 在 `next()` 前触发 `BeforeToolExecute`，在 `next()` 返回后触发 `AfterToolExecute`
- 如果 `BeforeToolExecute` 返回 `IsBlocked=true`，跳过实际工具调用

---

## 新增文件清单

| 文件                                                 | 层           | 说明                                               |
|----------------------------------------------------|-------------|--------------------------------------------------|
| `ManInBlack.AI.Abstraction/Hooks/HookPoint.cs`     | Abstraction | 挂载点枚举，定义 6 个生命周期节点                               |
| `ManInBlack.AI.Abstraction/Hooks/HookContext.cs`   | Abstraction | 传递给钩子脚本的上下文数据                                    |
| `ManInBlack.AI.Abstraction/Hooks/HookResult.cs`    | Abstraction | 钩子脚本的返回结果                                        |
| `ManInBlack.AI.Abstraction/Hooks/IHookExecutor.cs` | Abstraction | 钩子执行器接口                                          |
| `ManInBlack.AI/Configuration/HookSettings.cs`      | AI          | 单条钩子配置模型                                         |
| `ManInBlack.AI/Services/HookExecutor.cs`           | AI          | 钩子执行引擎实现                                         |
| `ManInBlack.AI/Middlewares/HookMiddleware.cs`      | AI          | 中间件层钩子，处理 BeforeLlmCall / AgentCompleted         |
| `ManInBlack.AI/ToolCallFilters/AgentLifecycleFilter.cs` | AI | 工具过滤器层钩子，处理 BeforeToolExecute / AfterToolExecute |
| `ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs` | AI          | 循环中间件，处理 AfterLlmCall / AllToolsCompleted        |

---

## 注意事项

- **Script 必须包含解释器前缀**：`Script` 字段是原始 shell 命令，不是文件路径。必须写 `"python script.py"` 而不是
  `"script.py"`
- **Script 不要重定向 stdin**：`HookContext` 经 stdin 传入脚本，框架写完即关闭 stdin（发 EOF）。因此 `Script` 命令
  不要用 `< /dev/null`、`| tee`、`> file` 等重定向 stdin 的写法（会断开或抢占 stdin，导致脚本读不到上下文）；
  多命令链 `a.py && b.py` 会让 `a.py` 独占 stdin、`b.py` 拿到空输入——若多个命令都需要上下文，应在单个脚本内部处理
- **IAsyncEnumerable 不阻塞流**：`HookMiddleware` 和 `AgentLoopMiddleware` 通过 `yield return` 流式转发 LLM
  响应，钩子执行不会阻塞流式输出
- **Hooks 配置每个 Scope 缓存一次**：`HookExecutor` 使用懒加载缓存 `_cachedHooks`，在首次调用 `ExecuteAsync`
  时加载全局和用户钩子，后续调用直接使用缓存
- **全局钩子工作目录**：全局钩子的工作目录为 `{RootPath}/hooks/`，用户钩子的工作目录为 `{workspace}/`
- **AfterToolExecute 不阻断流程**：`AgentLifecycleFilter` 中 `AfterToolExecute` 的返回结果被忽略，不会影响后续流程
- **ToolNames 过滤仅对工具级挂载点生效**：`BeforeToolExecute` 和 `AfterToolExecute` 会按 `ToolNames` 过滤，其他挂载点忽略此字段
- **日志默认精简**：Hook 执行明细（匹配、命令、返回）默认记录为 `Debug`；`Warning` 保留脚本异常，`Information` 仅保留关键事件（如阻断）
