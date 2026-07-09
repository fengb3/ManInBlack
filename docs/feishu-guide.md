# 飞书适配器

> 本文档是 CLAUDE.md 的子文档，Agent 在修改 Feishu Adaptor 相关代码前应先阅读此文档。

## 概述

飞书 IM bot via WebSocket + streaming cards. 通过 `FeishuCardSession` 订阅 EventBus 事件驱动飞书卡片 UI。一次 Agent 回复里：

- **推理 + 工具调用** 合并进同一张流式卡（`MergeCardView`）：reasoning 折叠块、工具折叠块按发生顺序纵向 append 到同一张卡，推理块内打字机流式。
- **文本输出(text)** 单独成卡（`CardView<LlmOutputViewModel>`），并作为合并卡的边界 —— 出现 text 即封口当前合并卡，之后的 reasoning+工具开新卡（避免 text 独立卡夹在中间、旧合并卡又向上追加造成 IM 显示顺序错乱）。
- **子 Agent 委托** 维持 `DelegationCardView`（全量重建模式，将来计划复用合并卡机制重构）。

`MergeCardView` 内部用 `Channel<CardOp>` + 单消费者串行处理所有飞书 API 调用（20ms 批量、内容更新去重、按 `sequence` 单调排序发送），保证 `EventBus` 并发回调下的线程安全与顺序一致。text 卡继续走全局 `CardUpdateScheduler`。卡片使用 JSON 2.0 + snake_case 序列化。

## 架构

不再使用独立的中间件处理卡片。飞书管道直接使用 `UseDefault()`，卡片逻辑完全在 `FeishuCardSession` 中通过 EventBus 事件驱动：

```
AgentLauncher.LaunchAsync
  └─ factory.RunAsync("feishu-agent", ..., configure)
       └─ FeishuCardSession.Subscribe()  ← 订阅 EventBus 事件（_gate 串行化所有回调）
            ├─ ModelContentEvent
            │    ├─ Reasoning → MergeCardView（新推理折叠块 + 流式更新）
            │    ├─ Text     → LlmOutputCardView（独立卡；并封口当前合并卡）
            │    └─ Completed → 关闭当前 text 卡（不关合并卡）
            ├─ BeforeToolExecuteEvent → MergeCardView append 工具块（DelegateToAgent → DelegationCardView）
            ├─ AfterToolExecuteEvent  → MergeCardView 更新工具结果（按 callId 路由到注册卡）
            ├─ AgentCompletedEvent    → 关闭所有合并卡 + 残留 text 卡（收尾）
            ├─ SubAgentStartedEvent   → 订阅子 Agent 事件（以 SubAgentId 为 key）
            └─ SubAgentCompletedEvent → 释放子 Agent 订阅，刷新 DelegationCardView
```

`FeishuCardSession` 封装完整的卡片生命周期：创建、流式更新、封口、收尾关闭、Dispose 订阅。`AgentLauncher` 的 configure 回调仅负责创建 session 并调用 `Subscribe()`。

## 关键组件

| 组件 | 职责 |
|------|------|
| `AgentLauncher` | 接收飞书消息，启动 Agent，创建 `FeishuCardSession` |
| `FeishuCardSession` | EventBus 订阅 + 卡片状态机（`_gate` 串行化全部回调），实现 `IDisposable` |
| `MergeCardView` | 推理+工具合并卡；`Channel<CardOp>` 单消费者串行处理，流式 append 块 + 内容更新去重 |
| `CardView<T>` | 泛型卡片视图基类；text 卡等通过 `BindMarkdown` 属性绑定走 `CardUpdateScheduler` |
| `CardUpdateScheduler` | 单例调度器，服务 text 卡的流式内容更新（20ms 去重、限流） |
| `CardService` | 飞书卡片 API 封装（创建/发送/append/内容更新/替换/关闭流式） |
| `ToolDisplayNames` | 工具方法名 → 中文显示名映射（本地精确 + MCP 模糊），供各卡片视图复用 |

## 卡片类型

| 视图 | 内容类型 | 触发事件 | 更新模式 |
|------|----------|----------|----------|
| `MergeCardView` | 推理 + 工具调用（连续段合并） | `ModelContentEvent (Reasoning)` / `BeforeToolExecuteEvent` / `AfterToolExecuteEvent` | 流式 append 块 + 元素内容流式更新 + 工具结果整面板替换 |
| `CardView<LlmOutputViewModel>` | 模型文本输出 | `ModelContentEvent (Text)` | `BindMarkdown` → `CardUpdateScheduler` 流式 |
| `DelegationCardView` | 子 Agent 委托 | `SubAgentStartedEvent` / `SubAgentCompletedEvent` + 子 Agent 事件 | 全量 `FullUpdateAsync`（待重构） |

### MergeCardView

合并卡是一张 `streaming_mode=true` 的卡，body 初始为空，内容块随事件动态 append：

```
Card body（流式）
├── CollapsiblePanel "🤔 琢磨琢磨"（推理块，lime-300，默认折叠）
│     └── Markdown（流式打字机更新，传全量累积内容）
├── CollapsiblePanel "📖 读取文件 完成"（工具块，green/indigo/red-100）
│     ├── Markdown（参数）
│     ├── Hr
│     └── Markdown（结果；占位"⏳ 执行中..."，结果到达后整面板替换）
├── CollapsiblePanel "🤔 琢磨琢磨"（下一轮推理 → 新块）
└── ...
```

更新策略（均在单消费者线程串行执行）：
- **新增推理块**：`AddElements` append 折叠面板，记录活动块 elementId；后续 reasoning token 累积全文后经 `UpdateElementStreamAsync`（`PutCard...Content`）流式刷新该元素。
- **新增工具块**：`AddElements` append 含参数 + 占位结果的折叠面板，按 `callId` 记录。
- **工具结果**：`ReplaceElementAsync` 整面板替换（标题变"完成/失败"、背景色随之变化、结果区填入）。
- **收尾**：`CloseStreamingAsync` 先 flush 所有待发更新，再 `PatchCardkit...Settings` 设 `streaming_mode: false`。

> 飞书流式卡在流式过程中支持调用新增组件/局部更新接口（官方确认），统一用 `sequence` 字段保证顺序。合并卡所有更新共享同一 `sequence` 计数器，消费者按 `Seq` 排序后逐个发送，从而保证 append 与内容更新不互相乱序。

### DelegationCardView

当父 Agent 调用 `DelegateToAgent` 工具时，`FeishuCardSession` 先封口当前合并卡（委托也是合并卡边界），再创建 `DelegationCardView`。子 Agent 的所有输出嵌套在同一张卡片中：

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

## 并发与顺序

- **`_gate`**：`FeishuCardSession` 用 `SemaphoreSlim(1,1)` 串行化全部事件回调 —— `EventBus.PublishAsync` 用 `Task.WhenAll` 并发分发，且工具调用是并行的，多个 `BeforeToolExecuteEvent` / `AfterToolExecuteEvent` 会并发到达。
- **`sequence` 单调**：同一张合并卡的所有更新（append / 内容更新 / 替换）走 `MergeCardView` 自带的串行通道，**不**与 `CardUpdateScheduler` 混用（否则会并发发出、破坏 sequence 顺序）。text 卡是不同物理卡，sequence 独立。
- **工具结果路由**：`_toolCallToCard` 按 `callId` 映射到注册它的那张合并卡，即便之后被 text/委托打断、活动卡已切换，结果仍能对号入座。

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

- `FeishuCardSession` / `MergeCardView` 内部使用 `GetAwaiter().GetResult()` 同步等待卡片初始化（订阅回调在并发非异步上下文中）。
- **text 输出是合并卡的边界**：出现 text 封口当前合并卡；纯 reasoning↔工具交织（无 text 穿插）则全部进同一张。
- **并行工具**：同一张合并卡内按 `callId` 索引多个工具块，并发到达、互不干扰。
- 工具结果超过 500 字符时截断显示。
- 子 Agent 与父 Agent 共享同一工作空间（通过 `RootUserId` 向上追溯到根用户）。
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
