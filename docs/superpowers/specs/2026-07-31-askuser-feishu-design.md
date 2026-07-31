# AskUser 飞书提问工具 设计

> 日期：2026-07-31
> 状态：待批准
> 关联：建立在已合入主干的「`[AiTool]` 复杂对象/数组参数支持」之上（`List<AskUserOption>` 入参依赖它）。
> 范围：**仅限 `demo/FeishuAdaptor` 项目**，不动 `src/ManInBlack.AI` 核心与源生成器。

## 背景与动机

需要让飞书 agent 在执行中「向用户提一个多选一/多选多的问题，等用户在飞书里点选，再把选择结果作为工具返回值交回 LLM 继续推理」——即 Claude Code `AskUserQuestion` 的飞书卡片镜像。

FeishuAdaptor 已具备完善的**卡片发送**基础设施（`CardService`、`ButtonElement`、`CallbackBehavior`、`FormElement`、`MultiSelectStaticElement`），但**没有卡片回传交互（按钮点击）的接收端**：全仓只搜到 `ImMessageReceiveEventHandler` / `ImMessageReadEventHandler` 两个事件处理器，无任何 `card.action` 回调处理器。这是本特性要补的核心缺口。

## 可行性证据（已核实）

| 能力 | 证据 |
|------|------|
| 发交互卡 | `CardService.CreateAsync(Card)`（`CardService.cs:18`）+ `SendMessageAsync(cardId, receiveIdType, receiveId)`（`CardService.cs:36`）|
| 按钮带回传值 | `ButtonElement.Behaviors: List<ActionBehavior>`（`InteractiveElements.cs:63`）+ `CallbackBehavior.Value: object?`（`CardElement.cs:178-183`）|
| 接收点击回调 | FeishuNetSdk `CardActionTriggerEventBodyDto`（`FeishuNetSdk.CallbackEvents`），`Action.Value: Dictionary<string,object>`（回传我们塞的值）、`Operator.UserId/OpenId`（谁点的）|
| 回调处理器接口 | `FeishuNetSdk.Services.ICallbackHandler<T1,T2,T3>`（消息体/事件体/响应体），可同步返回 `CardActionTriggerResponseDto`（toast/更新卡）|
| 回调送达 | webhook 模式 `app.UseFeishuEndpoint(...)`（`Program.cs:101-102`）会按事件类型路由到已注册的 `IEventHandler`/`ICallbackHandler`（与 `ImMessageReceiveEventHandler` 同机制）|
| 工具知道问谁 | `AgentFactory.RunAsync` 在 agent scope 内设 `agentContext.RootUserId`（`AgentFactory.cs:178`）；feishu-agent 以 `parentType="feishu_user"` 启动（`ImMessageReceiveEventHandler.cs:113-115`），故 `RootUserId` = 飞书 `user_id` |
| 工具拿到 ct | `agentContext.CancellationToken` 已赋值（`AgentFactory.cs:183`）|
| 复杂入参 | 已落地的源生成器能力：`List<AskUserOption>` 自动产出 `array of object` schema，运行时 `JsonElement.Deserialize<List<AskUserOption>>(ToolArgumentJsonOptions.Default)`（`ToolCallerEmitter.cs:270-273`）|

## 目标与非目标

**做**：
- 新增 `[AiTool] AskUserAsync`，定义在 FeishuAdaptor，参数 `question / List<AskUserOption> options / bool multiSelect / int timeoutSeconds`。
- 单选：问题 + 按钮组，点任一按钮**立即**返回该选项。
- 多选：问题 + 飞书原生 `multi_select_static`（置于 `form` 内）+ 提交按钮，点**提交**一次性返回全部选中项。
- 新增卡片回传交互处理器，把用户点击/提交解析回工具。
- 可配超时自动结束；agent 取消时干净释放。
- 顺带修复 FeishuAdaptor.Tests 现存编译错误（否则测试工程无法编译）。

**不做**：
- 不改 `src/ManInBlack.AI`、不改源生成器（复杂参数能力已就绪）。
- 不做框架级抽象（之前的「`IAskUserService` + 各适配器实现」方案废弃；本轮就是 FeishuAdaptor 内的具体实现）。
- 不做 e2e 真机联调（需真飞书，留作上线后人工验证）；本轮交付单元测试。

## 设计

### 1. 关联/阻塞机制：单例 PendingAskRegistry + TaskCompletionSource（核心）

工具运行在 agent 的 DI scope 内，而卡片回调到达时是**另一个独立的 webhook 请求 scope**。两者靠一个**单例**注册表打通：

- 工具生成 `requestId`（Guid），创建 `TaskCompletionSource<AskUserResult>(RunContinuationsAsynchronously)`，把 `{Tcs, MultiSelect, OptionsByValue, ExpiresAt, AskedUserId}` 存进单例 `PendingAskRegistry`，key = `requestId`。
- `requestId` 塞进每个交互元素的 `CallbackBehavior.Value`（单选按钮还额外带 `option`；多选提交键只带 `requestId`，选中项从 form 值取）。
- 工具 `await Task.WhenAny(tcs.Task, Task.Delay(timeout, linkedCt))` 阻塞。
- 回调处理器读 `Action.Value` → 取 `requestId` → `_registry.Resolve(requestId, 选中项)` → `TrySetResult` → 工具解开阻塞。

**为何不用 cardId 做关联键**：回调事件体不一定干净携带 cardkit 的 card_id；自己塞 `requestId` 最稳。
**为何不走 EventBus**：多一层间接 + 要管订阅生命周期；TCS 直达更简单。

### 2. 组件（均为新增，路径相对 `demo/FeishuAdaptor/`）

| 文件 | 职责 |
|------|------|
| `Tools/AskUserOption.cs` | `record AskUserOption`：`Label`（必填，按钮/选项文案）、`Description?`（辅助说明）、`Value`（回传值，默认=Label）。公共属性，供源生成器 schema 与 STJ 反序列化 |
| `Tools/PendingAskRegistry.cs` | `[ServiceRegister.Singleton]`。`ConcurrentDictionary<string, PendingAsk>`；`PendingAsk`（Tcs/MultiSelect/OptionsByValue/ExpiresAt/AskedUserId）；`AskUserResult`（`string[] SelectedValues`）。方法 `Register / TryGet / Resolve(幂等) / TryRemove` |
| `FeishuCard/AskUserCardBuilder.cs` | 按 `multiSelect` 构建 `Card`：<br>• 单选：标题 + 问题 `MarkdownElement` + 若干 `ButtonElement`（`Behaviors=[CallbackBehavior{Value={requestId,option}}]`）<br>• 多选：`FormElement` 内含问题 `MarkdownElement` + `MultiSelectStaticElement`（`name="opts"`，`Options` 由 `AskUserOption` 映射）+ 提交 `ButtonElement`（`FormActionType="submit"`，`Behaviors=[CallbackBehavior{Value={requestId}}]`） |
| `EventHandlers/CardActionCallbackHandler.cs` | 实现 FeishuNetSdk 卡片回调接口（`CardActionTriggerEventBodyDto`）。读 `Action.Value`/`Action.FormValue` → 解析 `requestId` + 选中项 → `_registry.Resolve` → 返回 `CardActionTriggerResponseDto`（toast「已收到你的选择」）。未知/过期/已解决 requestId 静默忽略 |
| `Tools/AskUserTool.cs` | `[ServiceRegister.Scoped] partial class AskUserTool`。构造注入 `CardService`/`PendingAskRegistry`/`AgentContext`/`ILogger`。`[AiTool] AskUserAsync(string question, List<AskUserOption> options, bool multiSelect=false, int timeoutSeconds=300)` |

### 3. `AskUserAsync` 流程

1. 校验 `options` 非空，否则返回 `提问失败：未提供可选项`。
2. 收件人 `userId = _agentContext.RootUserId`（`receiveIdType="user_id"`）。
3. `requestId = Guid.NewGuid().ToString("N")`；`var card = AskUserCardBuilder.Build(question, options, multiSelect, requestId)`。
4. `cardId = await _cardService.CreateAsync(card, ct)`。
5. `_registry.Register(requestId, new PendingAsk { ... })`。
6. `await _cardService.SendMessageAsync(cardId, "user_id", userId, ct)`。
7. 用 `CreateLinkedTokenSource(_agentContext.CancellationToken, timeoutCts)`（`timeoutCts` 按 `timeoutSeconds`）`CancelAfter`；`await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCt))`。
8. 分支：
   - `tcs.Task.IsCompletedSuccessfully` → 取 `AskUserResult`，返回 `用户选择了：{labels}`（多选用顿号连接）。
   - `linkedCt.IsCancellationRequested`：若 `_agentContext.CancellationToken` 触发 → `提问已被取消`；否则（超时）→ `用户未在 {N} 秒内作答（已超时）`。
   - finally：`_registry.TryRemove(requestId)`；释放 `linkedCts`。

> **不把 `CancellationToken` 声明为 `[AiTool]` 参数**：源生成器会把每个参数当 LLM 需提供的入参并尝试从 `Arguments` 反序列化（`CancellationToken` 走值类型分支会 `JsonElement.Deserialize<CancellationToken>` 出错）。改为方法内取 `_agentContext.CancellationToken`。这与现有工具（`FileTools` 等均不在 `[AiTool]` 签名里声明 ct）一致。

### 4. 回调处理器

- **单选**：`Action.Value = { ["requestId"]=..., ["option"]=optionValue }`。处理器取出 → 选中项 = `[optionValue]` → resolve。
- **多选（form 提交）**：提交键 `Action.Value = { ["requestId"]=... }`；选中项从 `Action.FormValue["opts"]`（或 `Action.Options`）取数组。
  > `FormValue` vs `Options` 的精确字段实现期对照 FeishuNetSdk 4.2.4 `ActionSuffix` 确认（已知两者都存在于 `ActionSuffix`）。
- **幂等**：飞书 ACK 超时会重推同一回调 → `TaskCompletionSource.TrySetResult` 对已解决实例返回 false，天然防重。
- 返回 `CardActionTriggerResponseDto`，toast 提示「已收到你的选择」。

### 5. 注册与接线

- `demo/FeishuAdaptor/Program.cs`：`AddManInBlack()`（`:66`）之后补一行 `services.AddToolHandlers();`。该扩展由源生成器针对 FeishuAdaptor 程序集生成（`internal`），注册本程序集内的 `[AiTool]` handler 与 declaration。需补对应 `using`（生成命名空间实现期确认）。
  > 现 `AddManInBlack()` 内已调 `services.AddToolHandlers()`（`DependencyInjection.cs:121`），但那只注册 `ManInBlack.AI` 自身的工具；FeishuAdaptor 自己的工具须由 FeishuAdaptor 的生成扩展注册。
- `AskUserTool` 标 `[ServiceRegister.Scoped]` → `AddAutoRegisteredServices()`（`Program.cs:80`）自动收。
- `PendingAskRegistry` 标 `[ServiceRegister.Singleton]` → 同上。
- `CardActionCallbackHandler`：仿 `ImMessageReceiveEventHandler`（无 `[ServiceRegister]`、不在 `Program.cs` 手动注册）——由 `AddFeishuNetSdk(...)` 自动发现 `IEventHandler`/`ICallbackHandler` 实现。实现期确认发现机制一致。

### 6. 可用范围

工具随 `ToolRegistry` 全局注册；`feishu` 与 `sub-agent` 两条管道都走 `ToolsMiddleware`，故**默认两者都能调用** `AskUser`。若需限定仅 feishu-agent 可用，需引入工具过滤（超出本轮范围）。

### 7. 错误处理汇总

| 场景 | 行为 |
|------|------|
| `options` 为空 | 返回 `提问失败：未提供可选项`（不发卡） |
| 超时 | 返回 `用户未在 {N} 秒内作答（已超时）`，移除 pending |
| agent 被取消（用户发新消息） | linkedCt 触发，返回 `提问已被取消`，移除 pending |
| 未知/过期/已解决 requestId 的回调 | 忽略 + toast「问题已过期」 |
| 飞书重推同一回调 | `TrySetResult` 幂等，重复无效 |
| 卡片发送失败 | 抛出 → 工具 `Error` 回传 LLM |
| （可选安全）`Operator.UserId` ≠ 提问对象 | 忽略该次点击，防他人代答 |

## 涉及文件

| 文件 | 变更 |
|------|------|
| `demo/FeishuAdaptor/Tools/AskUserOption.cs` | 新增 |
| `demo/FeishuAdaptor/Tools/PendingAskRegistry.cs` | 新增（含 `PendingAsk`、`AskUserResult`）|
| `demo/FeishuAdaptor/FeishuCard/AskUserCardBuilder.cs` | 新增 |
| `demo/FeishuAdaptor/EventHandlers/CardActionCallbackHandler.cs` | 新增 |
| `demo/FeishuAdaptor/Tools/AskUserTool.cs` | 新增 |
| `demo/FeishuAdaptor/Program.cs` | 改：补 `services.AddToolHandlers();` 与 `using` |
| `demo/FeishuAdaptor/FeishuAdaptor.Tests/` | 新增上述组件的单测；**修复** `CardServiceTests.cs` / `MergeCardViewTests.cs` 现存构造参数缺失（`CardService` 多了 `logger`，测试只传 2 参）|
| `docs/tools-guide.md` | 补 AskUser 用法与示例 |

`src/ManInBlack.AI.*`：**零改动**。

## 测试计划（`FeishuAdaptor.Tests`，该项目允许 NSubstitute）

1. **`AskUserCardBuilder`**：单选卡含按钮组且每个 `CallbackBehavior.Value` 带 `requestId`+`option`；多选卡为 `form` + `multi_select_static` + 提交键，提交键 Value 带 `requestId`。
2. **`PendingAskRegistry`**：register/resolve/remove 线程安全；重复 resolve 幂等（第二次返回 false/空）；过期条目可清。
3. **`AskUserTool`**（NSubstitute 造 `CardService`、`PendingAskRegistry`、`AgentContext`）：
   - 模拟 `registry` resolve → 工具返回 `用户选择了：{label}`（单/多选各一）。
   - 模拟超时（`timeoutSeconds` 极小 + 不 resolve）→ 返回超时串。
   - 模拟 agent 取消 → 返回取消串。
   - `options` 为空 → 返回失败串且不调 `CardService`。
4. **`CardActionCallbackHandler`**：构造带 `requestId`+`option` 的 `Action.Value` → 正确 resolve registry 并返回带 toast 的响应；未知 `requestId` → 不抛、registry 不变。

约定：FeishuAdaptor.Tests 用 NSubstitute（AGENTS.md 例外允许）；其余工程手写 fake。

## 部署前置（非代码，写进文档）

飞书应用后台须**订阅「卡片回传交互」事件**（`card.action.trigger`），并保持 webhook 接收方式（`app.UseFeishuEndpoint`），按钮点击回调才能送达。否则按钮点了无回调、工具将一直阻塞到超时。

## 风险与回滚

- **风险①：回调事件未订阅/未送达** → 工具必超时。缓解：文档明确前置；超时返回明确文案。
- **风险②：回调 handler 接口精确签名 / form 值字段** → 实现期对照 FeishuNetSdk 4.2.4 核实（`ICallbackHandler` 泛型、`ActionSuffix.FormValue` vs `Options`）。不影响整体架构。
- **风险③：pending 注册表进程内单例** → 进程重启丢失。但 agent 运行同样在进程内且短命，可接受；不跨实例/不持久化。
- **回归**：本轮不碰核心与源生成器；FeishuAdaptor 仅新增 + 一行注册。回归基线：`dotnet build demo/FeishuAdaptor` + `dotnet test demo/FeishuAdaptor/FeishuAdaptor.Tests` 全绿（先修现存编译错误）。
- **回滚**：全部新增文件 + `Program.cs` 一行，单提交可回退。
