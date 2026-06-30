# 飞书适配器

> 本文档是 CLAUDE.md 的子文档，Agent 在修改 Feishu Adaptor 相关代码前应先阅读此文档。

## 概述

飞书 IM bot via WebSocket + streaming cards. 通过 `FeishuCardSession` 订阅 EventBus 事件驱动飞书卡片 UI：
`ModelContentEvent` / `BeforeToolExecuteEvent` / `AfterToolExecuteEvent` / `SubAgentStartedEvent` / `SubAgentCompletedEvent` → 创建对应 ViewModel →
`CardView<T>.BindMarkdown()` 绑定 `PropertyChanged` → `CardUpdateScheduler`（单例，50/s 1000/min 限流）
批量更新飞书 API。卡片使用 JSON 2.0 + snake_case 序列化。

支持子 Agent 委托：子 Agent 的推理、文本输出和工具调用以话题式折叠面板嵌套在 `DelegateToAgent` 工具调用卡片中。

## 架构

不再使用独立的中间件处理卡片。飞书管道直接使用 `UseDefault()`，卡片逻辑完全在 `FeishuCardSession` 中通过 EventBus 事件驱动：

```
AgentLauncher.LaunchAsync
  └─ factory.RunAsync("feishu-agent", ..., configure)
       └─ FeishuCardSession.Subscribe()  ← 订阅 EventBus 事件
            ├─ ModelContentEvent (Text/Reasoning/Completed) → LLM 输出/推理卡片
            ├─ BeforeToolExecuteEvent → 工具调用卡片（DelegateToAgent → DelegationCardView）
            ├─ AfterToolExecuteEvent → 工具调用卡片（结果）
            ├─ SubAgentStartedEvent → 订阅子 Agent 事件（以 SubAgentId 为 key）
            └─ SubAgentCompletedEvent → 释放子 Agent 订阅，刷新 DelegationCardView
```

`FeishuCardSession` 封装了完整的卡片生命周期：创建、流式更新、关闭流式模式、Dispose 订阅。`AgentLauncher` 的 configure 回调仅负责创建 session 并调用 `Subscribe()`。

## 关键组件

| 组件 | 职责 |
|------|------|
| `AgentLauncher` | 接收飞书消息，启动 Agent，创建 `FeishuCardSession` |
| `FeishuCardSession` | EventBus 订阅 + 卡片状态管理，实现 `IDisposable` |
| `CardView<T>` | 泛型卡片视图基类，支持 `BindMarkdown` 属性绑定 |
| `CardUpdateScheduler` | 单例调度器，20ms 批量合并更新，限流保护 |
| `CardService` | 飞书卡片 API 封装（创建/更新/删除） |

## 卡片类型

| ViewModel | 内容类型 | 事件 |
|-----------|----------|------|
| `LlmReasoningViewModel` | 模型推理（折叠面板） | `ModelContentEvent (Reasoning)` |
| `LlmOutputViewModel` | 模型文本输出 | `ModelContentEvent (Text)` |
| `LlmToolExecutionViewModel` | 工具调用与结果 | `BeforeToolExecuteEvent` / `AfterToolExecuteEvent` |
| `DelegationViewModel` | 子 Agent 委托（话题式卡片） | `SubAgentStartedEvent` / `SubAgentCompletedEvent` + 子 Agent 事件 |

### DelegationCardView

当父 Agent 调用 `DelegateToAgent` 工具时，`FeishuCardSession` 创建 `DelegationCardView` 代替普通的 `ToolExecutionCardView`。子 Agent 的所有输出嵌套在同一张卡片中：

```
CollapsiblePanel (header: "🤖 委托 translator 中...")
├── 📋 任务描述
├── CollapsiblePanel (🤔 推理过程, collapsed)  ← 子 Agent 推理
├── 子 Agent 文本输出                            ← 子 Agent 输出
├── CollapsiblePanel (📖 读取文件 完成)          ← 子 Agent 工具调用
└── ...
完成时标题变为 "🤖 委托 translator 完成"，背景色变绿
```

更新策略：非流式卡片，通过 `FullUpdateAsync` 全量更新。文本流式事件（reasoning/text）仅累积状态，在结构事件（子工具开始/完成、子 Agent 完成）时统一刷新，避免频繁 API 调用。

## ID 类型

使用 `user_id`（`SenderId.UserId`）作为飞书 API 的 `receive_id`，配合 `SendToUserAsync("user_id", userId)` 调用。

## 子 Agent 配置

飞书 demo 注册了 translator 子 Agent，父 Agent 通过 `SubAgents` 声明委托关系：

```csharp
// 子 Agent 使用不包含 DelegationMiddleware 的 pipeline，防止递归
builder.Services.AddAgentDefinition(new AgentDefinition
{
    Name = "translator",
    Description = "翻译专家，擅长将文本翻译成各种语言",
    Instruction = "你是一个翻译专家...",
    PipelineName = "sub-agent"
});

builder.Services.AddAgentDefinition(new AgentDefinition
{
    Name = "feishu-agent",
    Instruction = "你是运行在飞书中的智能 agent",
    PipelineName = "feishu",
    SubAgents = ["translator"]
});

// 子 Agent pipeline：有文件工具和事件发布，无 DelegationMiddleware
factory.RegisterPipeline("sub-agent", builder => builder
    .Use<EventPublishingMiddleware>()
    .Use<FileToolsMiddleware>()
    .UseSimple());
```

子 Agent 的事件订阅在 `FeishuCardSession` 中自动处理：
1. `SubAgentStartedEvent` → 以 `SubAgentId` 为 key 订阅子 Agent 的 `ModelContentEvent`/`BeforeToolExecuteEvent`/`AfterToolExecuteEvent`
2. 子 Agent 事件 → 路由到 `DelegationCardView` 累积状态
3. `SubAgentCompletedEvent` → 刷新最终状态到卡片，释放子 Agent 订阅

## 注意事项

- `FeishuCardSession` 内部使用 `GetAwaiter().GetResult()` 同步等待卡片初始化（因为订阅回调在非异步上下文中）
- 工具调用后会重置 `_lastLlmType`，确保后续文本创建新卡片而非追加到旧卡片
- 工具结果超过 500 字符时截断显示
- 子 Agent 与父 Agent 共享同一工作空间（通过 `RootUserId` 向上追溯到根用户）
- 上传文件后发给 agent 的提示词为：`用户上传了文件 {fileName} 已经保存在了你的工作路径 {workspaceDir}，在你了解用户为何上传它之前，不要读取文件`（`AgentLauncher.BuildFileReceivedNotice`）。同时告知文件名与确切工作路径，并要求 agent 先了解用户意图、再决定是否读取。
- **用户上传的文件会落到该发送者自己的工作空间**（`{RootPath}/workspaces/{SelfHostUserId}/`）。文件下载发生在 Agent 运行之前的独立 DI scope，此时 `AgentContext` 尚未被 `AgentFactory` 填充，而 `IUserWorkspace`（`FileUserWorkspace`）依据 `AgentContext.RootUserId` 决定目录。因此 `HandleMessage` 通过 `AgentLauncher.ResolveWorkspaceDirectory(sp, userId)` 在解析 workspace 前先把真实发送者写入 `AgentContext.RootUserId/ParentId`——否则两者为空，所有用户的文件都会静默堆进「空字符串用户」的工作空间（表现为文件存到了别人的 workspace 编号下）。

---

## 阿里云迁移 runbook（JSON → SQLite）

以下步骤将阿里云服务器上旧的 JSON 文件数据迁移到 SQLite。

```bash
# 1. 发新二进制(含 SQLite 存储 + migrator + migrate-storage 参数)到服务器
scp mib-feishu.tar.gz aliyun:~ && ssh aliyun 'tar xzf mib-feishu.tar.gz -C /opt/mib-feishu && chmod -R 755 /opt/mib-feishu'
# 2. 停服
ssh aliyun 'systemctl stop mib-feishu'
# 3. 迁移:读 /root/.man-in-black/sessions + users → 生成 maninblack.db
ssh aliyun '/opt/mib-feishu/FeishuAdaptor migrate-storage'
# 4. 核对(journalctl -u mib-feishu 看汇总计数 / ls /root/.man-in-black/maninblack.db)
# 5. 起服
ssh aliyun 'systemctl start mib-feishu'
```

迁移工具为幂等操作，可安全重复运行。旧的 `sessions/` 和 `users/` 目录原地保留不删除，确认无误后手动清理即可。详见 [存储指南](./storage-guide.md)。

