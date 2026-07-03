# 飞书 @提及信息内联注入 — 设计文档

- 日期: 2026-07-03
- 状态: 已批准 (Approach A)
- 分支/工作树: `worktree-feishu-at-mentions`
- 相关文件: `demo/FeishuAdaptor/EventHandlers/ImMessageReceiveEventHandler.cs`

## 1. 背景与问题

飞书 adaptor 收到文本消息时,`ImMessageReceiveEventHandler.HandleMessage` 的 `"text"` 分支只提取
`{"text": ...}` 中的原始文本。当消息里含有 @提及时,正文里出现的是**占位符**(如 `@_user_1`),与
`Message.Mentions` 数组中的条目通过 `key` 一一对应。当前实现把占位符原样传给 agent,导致 LLM 看到的
是无意义的 `@_user_1`,无法知道被@的是谁。

## 2. 目标

在 `"text"` 消息进入 agent 之前,把每个 `@_user_N` 占位符**内联替换**为被@者的可读信息(名字 + 全部
可获取的标识字段),保持其在正文中的原位。

## 3. 非目标(范围护栏)

- **不修改第 25 行的 `p2p` 过滤**;群聊路由由另外的改动/agent 负责。注入逻辑本身对 p2p 与 group
  都可复用(它只依赖 `(text, mentions)`)。
- **只处理 `"text"` 类型**;`"post"` 富文本仍走现有 `unsupported` 分支。
- **不发起额外的飞书 API 调用**来补全用户资料;仅使用事件 payload 中已有的字段。
- 不改动 agent 运行管线、`FeishuCardSession`、文件下载等其他逻辑。

## 4. 数据模型(FeishuNetSdk 4.1.2)

`EventV2Dto<ImMessageReceiveV1EventBodyDto>` 中:

- `input.Event.Message.Mentions`:`MentionEvent` 集合(可能为 null/空)。XML 文档摘要:
  「被提及用户的信息,必填:否」。
- 每个 `ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent`:
  - `Key`:占位符,如 `@_user_1`
  - `Name`:被@者显示名
  - `Id`:用户标识对象,含 `OpenId` / `UserId` / `UnionId`(对应 JSON `open_id` / `user_id` / `union_id`)
  - `TenantKey`:租户标识
- 文本内容 `{"text":"@_user_1 你好"}` 中的 `@_user_1` 即对应某个 `MentionEvent.Key`。

## 5. 设计(Approach A:文件内静态助手)

在 `demo/FeishuAdaptor/EventHandlers/ImMessageReceiveEventHandler.cs` 中新增 `internal static` 助手
`ResolveMentions`,紧邻现有 `ResolveWorkspaceDirectory` / `BuildFileReceivedNotice` 两个静态助手,
沿用同一风格。

### 5.1 数据流

`HandleMessage` 的 `"text"` 分支:

```csharp
case "text":
{
    var doc = JsonDocument.Parse(messageContent);
    var text = doc.RootElement.GetProperty("text").GetString()!;
    result = ResolveMentions(text, input.Event.Message?.Mentions);
    break;
}
```

管线其余部分不变 —— `result` 仍作为 `userLlmInput` 喂给 agent。

### 5.2 签名与行为

```csharp
internal static string ResolveMentions(
    string text,
    IEnumerable<ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent>? mentions)
```

> 集合元素的确切 C# 类型名以 SDK 实际为准(实现时核对);上述为依据 XML 文档推断。

- 若 `mentions` 为 null/空,原样返回 `text`(完全保持现有行为)。
- 对每个 `Key` 非空的 mention,计算其格式化串,执行 `text = text.Replace(key, formatted)`。
- 各 `Key` 在单条消息内唯一(`@_user_1`、`@_user_2`…),故顺序无关、逐个替换安全,且格式化输出不会
  产生新的 `Key` 造成级联。
- `Key` 不在文本中出现的 mention 跳过;文本中无对应 mention 条目的 `@_user_N` 占位符保持原样。
  **任何情况下都不抛异常。**

## 6. 单个 mention 的格式

固定字段顺序,**只输出非空字段**(`tenant_key` 按需求不纳入):

```
@<Name>(open_id:<v>, user_id:<v>, union_id:<v>)
```

- 只包含非空字段(外部用户常无 `user_id`);仅 `open_id` 时渲染为 `@张三(open_id:ou_zhang)`。
- 示例(原文 `@_user_1 你好,请把报告发给@_user_2`):

  ```
  @张三(open_id:ou_zhang, user_id:zhangsan, union_id:on_zhang) 你好,请把报告发给@李四(open_id:ou_li, user_id:lisi, union_id:on_li)
  ```

## 7. 边界情况(默认处理)

- `Name` 缺失 → 标签回退为 `未知用户`。
- `@所有人` / mention-all → `Id` 可能为 `"all"` 或字段为空;输出存在的字段,若 id 字段全空则仅
  `@<Name>`(如 `@所有人`)。
- 被@的是 bot 自身 → 与普通用户一致地输出其信息(不做特例),符合「带上所有可获取信息」。
- 字段顺序固定:`open_id, user_id, union_id`(不含 `tenant_key`)。

## 8. 测试

在 `test/FeishuAdaptor.Tests/` 新增单元测试,仿照 `AgentLauncherFileWorkspaceTests.cs`
(`internal static` 助手经 `InternalsVisibleTo("FeishuAdaptor.Tests")` 可测,纯字符串变换,无需 mock 飞书):

- 单个 @提及
- 多个 @提及
- 外部用户(无 `user_id`,验证仅输出存在的字段)
- `@所有人`
- 无 mentions / null(回退原文本)
- 文本中存在无 mention 条目的占位符(保持原样)
- `Name` 缺失(回退 `未知用户`)

## 9. 影响面与兼容性

- 改动文件:`ImMessageReceiveEventHandler.cs`(新增助手 + `"text"` 分支一处调用),
  `test/FeishuAdaptor.Tests/` 新增测试文件。
- 对现有 p2p 行为**向后兼容**:无 mentions 时输出与今天完全一致。
- 不触碰 `p2p` 过滤、消息分发、卡片会话、文件下载等任何其他路径。
