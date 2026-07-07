# Hook / 观察者 解耦设计：双 key 通道隔离

- 日期：2026-07-07
- 状态：已确认，待写实现计划
- 相关代码：`src/ManInBlack.AI/Services/EventBus.cs`、`src/ManInBlack.AI/Middlewares/HookMiddleware.cs`、`src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs`、`src/ManInBlack.AI/ToolCallFilters/AgentLifecycleFilter.cs`

## 1. 背景

当前 hook（用户自定义钩子脚本）和外部观察者（飞书卡片、AgentConsole、日志等）共用同一条 `EventBus` 广播通道，按 `AgentId` 做 key。`EventBus.PublishAsync` 内部是 `Task.WhenAll(snapshot.Select(h => h(evt, ct)))`，对同一个 key + 事件类型 fan-out 全部订阅者并 `await` 全部完成。

这带来两个问题：

1. **延迟耦合 + 语义耦合**：hook 本质是「我要拿到结果才能继续」的拦截器（`BeforeLlmCall` 注入文本、`BeforeToolExecute` 阻断工具），结果靠 **publish → await → 读事件对象上的副作用** 回传（`HookMiddleware` 读 `evt.InjectedTexts`，`AgentLifecycleFilter` 读 `evt.IsBlocked`）。但同一事件还被观察者订阅（飞书卡片走网络往返），于是 hook 的 `await PublishAsync` 被迫等完所有非 hook 订阅者。
2. **观察者炸链**：`Task.WhenAll` 任意一个 handler 抛错就让整个 publish 抛 `AggregateException`。调用方（`AgentLifecycleFilter.cs:49`、`HookMiddleware.cs:140`、流式循环里的 `EventPublishingMiddleware.cs:45`）都没兜底 → **一个 UI/日志订阅者抛错就会打断整个 agent run（连模型流一起断）**。

## 2. 目标

- **G1**：hook 取结果的路径不再被观察者拖慢、不再与观察者耦合。
- **G2**：任一观察者抛错**只记日志**，不让 `PublishAsync` 抛、不向上游传播、不打断主链路。
- **G3**：保持现有 hook 能力（注入 / 阻断 / 各挂载点通知）与观察者能力（飞书卡片、Console 渲染）行为不变。
- **G4**：demo（`FeishuAdaptor` / `AgentConsole` / `GitHubAdaptor`）与观察者订阅侧**零改动**。

## 3. 非目标（本次不做）

- 不把 hook 改成「直接调用 `IHookExecutor`」的 RPC 模型（那是备选方案 (b)，本次未选）。hook 仍是 `EventBus` 订阅者，只是换到独立 key。
- 不为 `ModelContentEvent` 流式发布引入 fire-and-forget。若以后发现飞书慢 I/O 逐 chunk 阻塞模型流，再加 fire-and-forget 重载。
- 不改 hook 脚本契约（`HookContext` / `HookResult` / `IHookExecutor` 接口不变）。

## 4. 设计：双 key 通道隔离

引入两条互不相见的 lane：

- **观察者 lane**：key = `AgentId`（**不变**）。飞书卡片、Console、SubAgent 订阅、`ModelContentEvent` 全在这里。
- **hook lane**：key = `EventBus.HookKey(agentId)` = `"{AgentId}::hook"`（**新增**）。**全部 6 个 hook point 的订阅都走这个 key**。

`EventBus` 提供静态 helper 消掉 magic-string 脚枪：

```csharp
public static string HookKey(string agentId) => $"{agentId}::hook";
```

观察者仍用 `AgentId` 原值，无需 helper。

### 4.1 订阅侧映射

| 组件 | 订阅事件 | key |
|---|---|---|
| `HookMiddleware` | `BeforeLlmCall` / `AfterLlmCall` / `BeforeToolExecute` / `AfterToolExecute` / `AllToolsCompleted` / `AgentCompleted`（全部 hook） | `HookKey(agentId)` |
| `FeishuCardSession` | `ModelContent` / `BeforeToolExecute` / `AfterToolExecute` / `SubAgent*` | `AgentId`（不动） |
| `AgentConsole` | 同上 | `AgentId`（不动） |
| `DelegationTools` 子 agent 订阅 | `ModelContent` / `BeforeToolExecute` / `AfterToolExecute` | `AgentId`（不动） |

### 4.2 发布侧映射

每个事件按「有没有 hook 订阅 / 有没有观察者订阅」决定发哪个 key：

| 事件 | 发 `HookKey` | 发 `AgentId` | 发布方 |
|---|:---:|:---:|---|
| `BeforeLlmCallEvent` | ✓ | — | `HookMiddleware` |
| `AfterLlmCallEvent` | ✓ | — | `AgentLoopMiddleware` |
| `BeforeToolExecuteEvent` | ✓（await，读 `IsBlocked`） | ✓（UI「调用中」） | `AgentLifecycleFilter` |
| `AfterToolExecuteEvent` | ✓ | ✓ | `AgentLifecycleFilter` |
| `AllToolsCompletedEvent` | ✓ | — | `AgentLoopMiddleware` |
| `AgentCompletedEvent` | ✓ | — | `HookMiddleware` |
| `ModelContentEvent` | — | ✓ | `EventPublishingMiddleware`（不动） |
| `SubAgentStartedEvent` / `SubAgentCompletedEvent` | — | ✓ | `DelegationTools`（不动） |

> `BeforeLlmCall` / `AfterLlmCall` / `AllToolsCompleted` / `AgentCompleted` 当前没有观察者订阅，只发 hook lane；将来若观察者要订阅，再加 `AgentId` 发布即可（发到无订阅者的 key 是 no-op，无副作用）。

### 4.3 `BeforeToolExecute` 顺序硬约束

`AgentLifecycleFilter` 必须按以下顺序，保证阻断判断夹在两条 lane 之间、且观察者看到最终状态：

```
1. await bus.PublishAsync(HookKey(key), beforeEvt)   // hook 跑完，设置 evt.IsBlocked
2. 读 beforeEvt.IsBlocked
3. await bus.PublishAsync(key, beforeEvt)            // 观察者看到带 IsBlocked 的事件
4. if (beforeEvt.IsBlocked) { context.Error = ...; return; }
5. await next(context)                                // 执行工具
6. await bus.PublishAsync(HookKey(key), afterEvt); await bus.PublishAsync(key, afterEvt)
```

> 步骤 3 始终执行（即使被阻断），保持「观察者总能收到 `BeforeToolExecuteEvent`」的现有行为；事件上的 `IsBlocked` 字段让观察者可识别阻断态。

### 4.4 事件类型：不变

`BeforeLlmCallEvent.InjectedTexts` / `InjectTarget`、`BeforeToolExecuteEvent.IsBlocked` / `BlockReason` 这些「靠 mutate 回传结果」的字段**保留**。它们现在是 hook lane 内部的结果载体契约，且 hook lane 上只有 `HookMiddleware` 一个订阅者，可控。无需删除或重命名。

## 5. 错误隔离（G2，两条 lane 都做）

`EventBus.PublishAsync` 改为 **per-handler try/catch + log**：每个 handler 独立包一层，单个 handler 抛错只记录日志，不影响其他 handler、不让 `Task.WhenAll` 失败、不向调用方抛。

```csharp
// 概念示意
var tasks = snapshot.Select(h => InvokeIsolated(h, evt, ct));
await Task.WhenAll(tasks);

async Task InvokeIsolated(EventHandlerDelegate<TEvent> h, TEvent evt, CancellationToken ct)
{
    try { await h(evt, ct); }
    catch (Exception ex) { /* 记日志，不向上抛 */ }
}
```

要点：
- **观察者 lane 必须隔离**（这是 G2 的核心）。
- **hook lane 也隔离**（防御性；正常情况下 `HookExecutor.ExecuteSingleScript` 已逐脚本 try/catch，hook 不会向外抛，但隔离一层更稳）。
- 调用方（`AgentLifecycleFilter` / `HookMiddleware` / `EventPublishingMiddleware`）因此**不再需要**自己包 try/catch 兜底。
- `Subscribe`/`PublishAsync` 的公开签名与返回值（`Task`）不变。

## 6. 组件改动清单

| 文件 | 改动 |
|---|---|
| `EventBus.cs` | ① per-handler 错误隔离（try/catch + log）；② 新增 `HookKey(agentId)` 静态 helper；③ 让 bus 能记日志（推荐：改实例 Singleton + 注入 `ILogger<EventBus>`，把 static `EventBus<TEvent>` 的存储搬到实例；备选：static logger sink。实现计划阶段定） |
| `HookMiddleware.cs` | ① 全部 hook 订阅改 `HookKey(key)`；② `BeforeLlmCall` / `AgentCompleted` 发布改 `HookKey(key)`；③ 其余逻辑（注入 system prompt、循环外层位置）不变 |
| `AgentLoopMiddleware.cs` | `AfterLlmCall` / `AllToolsCompleted` 发布改 `HookKey(key)`（仅 key 串变化，**不动循环逻辑**） |
| `AgentLifecycleFilter.cs` | `BeforeToolExecute`：先发 `HookKey`（await 读 `IsBlocked`）→ 发 `AgentId` → 判定阻断；`AfterToolExecute`：发 `HookKey` + `AgentId` |
| 事件类型 | **不变** |
| demo / 观察者 | **不变** |
| `EventPublishingMiddleware.cs` / `DelegationTools.cs` | **不变** |

## 7. 测试

- `EventBusTests`
  - 新增：单个 handler 抛异常 → 其余 handler 照常完成；`PublishAsync` 不抛；异常被记录。
  - 新增：`HookKey` 隔离 —— 发 `HookKey` 不触达 `AgentId` 订阅者，反之亦然。
- `HookMiddlewareTests`
  - 订阅 key 改 `HookKey`；`BeforeLlmCall` 经 `HookKey` 发布后注入文本仍生效。
- `AgentLifecycleFilterTests`
  - `BeforeToolExecute` 阻断经 `HookKey` 发布生效；观察者（`AgentId`）在 hook 之后仍收到事件。
- `AgentLoopMiddlewareTests`
  - `AfterLlmCall` / `AllToolsCompleted` 订阅/发布 key 同步改为 `HookKey`。
- 既有测试中**模拟 hook** 的订阅（如 `AgentLifecycleFilterTests` 里设 `IsBlocked` 的订阅）须改用 `HookKey`；模拟观察者的订阅保持 `AgentId`。

验证要点（G2 回归）：观察者抛异常时 —— (1) 其他观察者照常完成；(2) `PublishAsync` 不抛；(3) `AgentLifecycleFilter` / `HookMiddleware` / 模型流不被打断。

## 8. 风险与权衡

- **magic-string key 脚枪**：发布方写错 key（比如忘了用 `HookKey`）→ hook 静默不跑。靠 `HookKey(agentId)` helper 统一收口 + 测试覆盖兜底。
- **多一次 publish 的样板**：`BeforeToolExecute` / `AfterToolExecute` 各发两条 lane。可接受（只有这两个事件双发）。
- **保留 mutate-回传契约**：hook 结果仍靠 mutate 事件字段回传（隐式契约），而非显式返回值。这是双 key 方案相对「直调」方案 (b) 的取舍 —— 换来 hook 逻辑全集中在 `HookMiddleware`、demo 零改动、改动面最小。hook lane 单一订阅者，契约可控。
