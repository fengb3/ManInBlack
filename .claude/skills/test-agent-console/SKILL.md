---
name: test-agent-console
description: >
  测试 AgentConsole demo 的完整流程。当用户说 测试 agent console、跑一下 AgentConsole、
  test agent console、验证 console demo 时触发。覆盖构建、运行、工具调用、子 Agent 委托、
  长上下文压力测试、错误恢复等场景。
---

# 测试 AgentConsole

验证 ManInBlack 框架通过控制台 demo 的端到端功能。

## 前置条件

- `~/.man-in-black/settings.json` 已配置（Providers、ModelChoices、Agents）
- `Agents` 中必须包含 `console-agent`（PipelineName: default）和 `translator`（PipelineName: sub-agent）
- .NET 10 SDK

## 构建与运行

```bash
# 构建
dotnet build demo/AgentConsole/AgentConsole.csproj

# 运行（必须加 MSYS_NO_PATHCONV=1，否则 Git Bash 会把 /new 转成路径）
MSYS_NO_PATHCONV=1 dotnet run --project demo/AgentConsole -- "<提示词>"
```

## 关键注意事项

### 1. Git Bash 路径转换

在 Windows Git Bash 环境下，`/new`、`/clear` 等以 `/` 开头的参数会被自动转为
`C:/Program Files/Git/new`。**必须**设置 `MSYS_NO_PATHCONV=1` 环境变量。

### 2. 工作空间隔离

Agent 的文件工具（Read/Write/Glob）运行在隔离的用户工作空间中，不是项目目录。
路径为 `~/.man-in-black/workspaces/{userId}/`。

- `userId` 由 `~/.man-in-black/users/userIdMap.json` 中的映射决定
- 控制台模式下 parentId 为 `"console"`，对应的 userId 可从 userIdMap 查到
- 测试文件需要手动复制到工作空间才能被 Agent 访问

### 3. 会话持久化

- `ReadPersistenceMiddleware` 处理 `/new`、`/clear`、`/reset` 命令，创建新会话
- 每次运行 AgentConsole 是独立进程，但共享同一个 session（通过 userId 映射）
- 不发 `/new` 则连续运行的请求会累积上下文

### 4. RetryMiddleware 行为

- 包裹整个 AgentLoop，仅当 **尚未 yield 任何内容** 时才能重试
- Agent 循环中第二轮之后的 API 失败无法重试（已 yield 过）
- 此时 RetryMiddleware 会先 yield 错误消息（`API 请求失败，已无法重试...`）再 throw
- 进程会 crash（AgentConsole 无顶层 try-catch），但至少 console 能看到错误原因

## 测试场景

按以下顺序执行，覆盖核心功能到长上下文压力：

### 场景 1：基础对话

```bash
MSYS_NO_PATHCONV=1 dotnet run --project demo/AgentConsole -- "你好，请用一句话介绍你自己"
```

验证：模型正常响应，reasoning/text 内容正确显示，Token 用量输出。

### 场景 2：工具调用

```bash
# 先把测试文件放入工作空间
cp demo/AgentConsole/test-input.txt ~/.man-in-black/workspaces/<userId>/

MSYS_NO_PATHCONV=1 dotnet run --project demo/AgentConsole -- "列出当前目录的文件，读取 test-input.txt 的内容"
```

验证：Glob、Read 工具调用正常，BeforeToolExecuteEvent/AfterToolExecuteEvent 事件流输出。

### 场景 3：子 Agent 委托

```bash
MSYS_NO_PATHCONV=1 dotnet run --project demo/AgentConsole -- "把 test-input.txt 的内容翻译成英文，请委托给 translator 子 Agent"
```

验证：DelegateToAgent 调用，SubAgentStartedEvent/SubAgentCompletedEvent 输出，子 Agent 的 ModelContentEvent 正常。

### 场景 4：文件写入

```bash
MSYS_NO_PATHCONV=1 dotnet run --project demo/AgentConsole -- "读取 test-input.txt 并写一份分析报告保存为 report.md"
```

验证：Write 工具调用成功，文件实际写入工作空间。

### 场景 5：会话重置

```bash
MSYS_NO_PATHCONV=1 dotnet run --project demo/AgentConsole -- "/new"
```

验证：输出"已重置对话"，后续请求开始新会话。

### 场景 6：长上下文压力测试

不发 `/new`，连续运行多轮复杂任务累积上下文。每轮选用多工具调用的任务：

```bash
MSYS_NO_PATHCONV=1 dotnet run --project demo/AgentConsole -- "<复杂多步骤任务>"
```

推荐任务类型（按复杂度递增）：
- 文件分析（Glob + 多次 Read + Write）
- 代码生成 + 语法检查（Write + RunBash py_compile）
- 系统巡检（多次 RunBash + Write 长报告）
- 代码审查 + 重构（多次 Read + Edit + RunBash）

每轮结束后记录 Token 用量（input/output/cached），观察：
- 缓存命中率是否稳定
- 模型是否在某个 token 阈值后断连（之前在 ~200k tokens 时偶发 deepseek 断连）
- RetryMiddleware 错误消息是否正常输出

### 场景 7：子 Agent 翻译委托（长上下文）

在累积了较长上下文后测试子 Agent 委托，验证长上下文下子 Agent 仍能正常工作。

## 检查清单

- [ ] 构建成功（0 error）
- [ ] 基础对话正常
- [ ] RunBash 工具调用
- [ ] Read/Glob/Write 文件工具
- [ ] DelegateToAgent 子 Agent 委托
- [ ] EventBus 事件流（ModelContentEvent、ToolExecuteEvent、SubAgentEvent）
- [ ] `/new` 会话重置
- [ ] Token 用量统计
- [ ] 长上下文（>100k tokens）稳定运行
- [ ] API 断连时错误消息可见
