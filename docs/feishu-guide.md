# 飞书适配器

> 本文档是 CLAUDE.md 的子文档，Agent 在修改 Feishu Adaptor 相关代码前应先阅读此文档。

## 概述

飞书 IM bot via WebSocket + streaming cards. 通过 `FeishuCardSession` 订阅 EventBus 事件驱动飞书卡片 UI：
`ModelContentEvent` / `BeforeToolExecuteEvent` / `AfterToolExecuteEvent` → 创建对应 ViewModel →
`CardView<T>.BindMarkdown()` 绑定 `PropertyChanged` → `CardUpdateScheduler`（单例，50/s 1000/min 限流）
批量更新飞书 API。卡片使用 JSON 2.0 + snake_case 序列化。

## 架构

不再使用独立的中间件处理卡片。飞书管道直接使用 `UseDefault()`，卡片逻辑完全在 `FeishuCardSession` 中通过 EventBus 事件驱动：

```
AgentLauncher.LaunchAsync
  └─ factory.RunAsync("feishu-agent", ..., configure)
       └─ FeishuCardSession.Subscribe()  ← 订阅 EventBus 事件
            ├─ ModelContentEvent (Text/Reasoning/Completed) → LLM 输出/推理卡片
            ├─ BeforeToolExecuteEvent → 工具调用卡片（开始）
            └─ AfterToolExecuteEvent → 工具调用卡片（结果）
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

## ID 类型

使用 `user_id`（`SenderId.UserId`）作为飞书 API 的 `receive_id`，配合 `SendToUserAsync("user_id", userId)` 调用。

## 注意事项

- `FeishuCardSession` 内部使用 `GetAwaiter().GetResult()` 同步等待卡片初始化（因为订阅回调在非异步上下文中）
- 工具调用后会重置 `_lastLlmType`，确保后续文本创建新卡片而非追加到旧卡片
- 工具结果超过 500 字符时截断显示
