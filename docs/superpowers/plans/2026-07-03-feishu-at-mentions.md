# 飞书 @提及信息内联注入 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把飞书 `"text"` 消息里的 `@_user_N` 占位符内联替换为被@者的可读信息(`@名字(open_id:.., user_id:.., union_id:..)`),让 agent 看懂被@的是谁。

**Architecture:** 在 `AgentLauncher` 上新增两个 `internal static` 方法(`ResolveMentions` + `FormatMention`),与现有 `ResolveWorkspaceDirectory` / `BuildFileReceivedNotice` 同处一文件、同一风格;`HandleMessage` 的 `"text"` 分支调用之。纯函数,对 p2p/group 均可复用,不触碰 `p2p` 过滤。

**Tech Stack:** C# / .NET 10、FeishuNetSdk 4.1.2、xUnit。

**对应设计文档:** `docs/superpowers/specs/2026-07-03-feishu-at-mentions-design.md`

---

## 关键事实(已实测,FeishuNetSdk 4.1.2)

- `Message.Mentions` 类型为 **`MentionEvent[]`**(可能为 `null`)。
- 元素类型全名(源码中用 `.` 引用嵌套类型):**`FeishuNetSdk.Im.Events.ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent`**。无参构造,属性均可 set:
  - `Key : string`、`Name : string`、`TenantKey : string`
  - `Id : FeishuNetSdk.Core.UserIdSuffix`(可能为 `null`)
- `FeishuNetSdk.Core.UserIdSuffix` 无参构造,属性均可 set:`OpenId : string`、`UserId : string`、`UnionId : string`。
- 文本内容 `{"text":"@_user_1 你好"}` 中的 `@_user_1` 即某 `MentionEvent.Key`,逐个 `string.Replace(key, formatted)` 即可。

## 文件结构

- **Modify:** `demo/FeishuAdaptor/EventHandlers/ImMessageReceiveEventHandler.cs`
  - 在 `AgentLauncher` 类中新增 `ResolveMentions` + `FormatMention`(紧邻 `BuildFileReceivedNotice`)。
  - `HandleMessage` 的 `"text"` 分支:`result = text;` → `result = ResolveMentions(text, input.Event.Message?.Mentions);`
- **Create:** `test/FeishuAdaptor.Tests/ResolveMentionsTests.cs` —— `ResolveMentions` 的纯函数单元测试。

---

## Task 1: 实现 ResolveMentions(TDD:先测试后实现)

**Files:**
- Create: `test/FeishuAdaptor.Tests/ResolveMentionsTests.cs`
- Modify: `demo/FeishuAdaptor/EventHandlers/ImMessageReceiveEventHandler.cs`(在 `AgentLauncher` 类内、`BuildFileReceivedNotice` 方法之后、`HandleMessage` 之前插入两个方法)

- [ ] **Step 1: 写失败测试**

创建 `test/FeishuAdaptor.Tests/ResolveMentionsTests.cs`,完整内容:

```csharp
using FeishuAdaptor.EventHandlers;
using FeishuNetSdk.Core;
using FeishuNetSdk.Im.Events;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// 验证 AgentLauncher.ResolveMentions:把文本里的 @_user_N 占位符
/// 内联替换为被@者的可读信息(名字 + 全部可获取的标识字段,只输出非空)。
/// </summary>
public class ResolveMentionsTests
{
    private static ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent Mention(
        string key,
        string? name = "某用户",
        string? openId = null,
        string? userId = null,
        string? unionId = null) =>
        new()
        {
            Key = key,
            Name = name!,
            Id = new UserIdSuffix { OpenId = openId!, UserId = userId!, UnionId = unionId! }
        };

    [Fact]
    public void 单个提及_替换为名字加全部标识()
    {
        var mentions = new[]
        {
            Mention("@_user_1", "张三", "ou_zhang", "zhangsan", "on_zhang")
        };

        var result = AgentLauncher.ResolveMentions("@_user_1 你好", mentions);

        Assert.Equal("@张三(open_id:ou_zhang, user_id:zhangsan, union_id:on_zhang) 你好", result);
    }

    [Fact]
    public void 多个提及_各自替换()
    {
        var mentions = new[]
        {
            Mention("@_user_1", "张三", "ou_zhang", "zhangsan", "on_zhang"),
            Mention("@_user_2", "李四", "ou_li", "lisi", "on_li")
        };

        var result = AgentLauncher.ResolveMentions("@_user_1 把报告发给@_user_2", mentions);

        Assert.Equal(
            "@张三(open_id:ou_zhang, user_id:zhangsan, union_id:on_zhang) 把报告发给@李四(open_id:ou_li, user_id:lisi, union_id:on_li)",
            result);
    }

    [Fact]
    public void 外部用户_缺user_id_只输出存在的字段()
    {
        var mentions = new[]
        {
            Mention("@_user_1", "李四", openId: "ou_li", unionId: "on_li")
        };

        var result = AgentLauncher.ResolveMentions("@_user_1 hi", mentions);

        Assert.Equal("@李四(open_id:ou_li, union_id:on_li) hi", result);
    }

    [Fact]
    public void 所有人_openid为all_只输出名字()
    {
        var mentions = new[] { Mention("@_user_1", "所有人", openId: "all") };

        var result = AgentLauncher.ResolveMentions("@_user_1 大家注意", mentions);

        Assert.Equal("@所有人 大家注意", result);
    }

    [Fact]
    public void mentions为null_原样返回()
    {
        var result = AgentLauncher.ResolveMentions("你好", null);
        Assert.Equal("你好", result);
    }

    [Fact]
    public void 空集合_原样返回()
    {
        var result = AgentLauncher.ResolveMentions(
            "你好",
            Array.Empty<ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent>());
        Assert.Equal("你好", result);
    }

    [Fact]
    public void 占位符无对应mention_保持原样()
    {
        var mentions = new[]
        {
            Mention("@_user_1", "张三", "ou_zhang", "zhangsan", "on_zhang")
        };

        var result = AgentLauncher.ResolveMentions("@_user_1 你好 @_user_2", mentions);

        Assert.Equal(
            "@张三(open_id:ou_zhang, user_id:zhangsan, union_id:on_zhang) 你好 @_user_2",
            result);
    }

    [Fact]
    public void 名字缺失_回退未知用户()
    {
        var mentions = new[] { Mention("@_user_1", name: null, openId: "ou_x") };

        var result = AgentLauncher.ResolveMentions("@_user_1 hi", mentions);

        Assert.Equal("@未知用户(open_id:ou_x) hi", result);
    }

    [Fact]
    public void 多于十个提及_user_1不误伤user_10()
    {
        // @_user_1 是 @_user_10 的子串,必须先替换长的,否则 @_user_10 被破坏成 <user1 的串>0
        var mentions = new[]
        {
            Mention("@_user_1", "甲", "ou_1"),
            Mention("@_user_10", "乙", "ou_10")
        };

        var result = AgentLauncher.ResolveMentions("at@_user_10 and @_user_1", mentions);

        Assert.Equal("at@乙(open_id:ou_10) and @甲(open_id:ou_1)", result);
    }
}
```

- [ ] **Step 2: 运行测试,确认失败(方法不存在)**

Run:
```bash
dotnet test test/FeishuAdaptor.Tests/FeishuAdaptor.Tests.csproj --filter "FullyQualifiedName~ResolveMentionsTests"
```
Expected: 编译失败,`error CS0117: 'AgentLauncher' does not contain a definition for 'ResolveMentions'`(测试先行,实现尚未写)。

- [ ] **Step 3: 实现 ResolveMentions + FormatMention**

在 `demo/FeishuAdaptor/EventHandlers/ImMessageReceiveEventHandler.cs` 的 `AgentLauncher` 类中,**紧接 `BuildFileReceivedNotice` 方法之后、`HandleMessage` 之前**插入:

```csharp
    /// <summary>
    /// 把文本中的 @_user_N 占位符内联替换为被@者的可读信息:
    /// <c>@名字(open_id:.., user_id:.., union_id:..)</c>,只输出非空字段(<c>tenant_key</c> 不纳入)。
    /// mentions 为 null 时原样返回。对 p2p / group 均可复用。
    /// </summary>
    internal static string ResolveMentions(
        string text,
        IEnumerable<ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent>? mentions)
    {
        if (mentions is null)
            return text;

        // 按 key 长度降序替换:避免 @_user_1 误伤 @_user_10(消息含 10+ 个 @提及时)
        foreach (var mention in mentions
                     .Where(m => !string.IsNullOrEmpty(m.Key))
                     .OrderByDescending(m => m.Key.Length))
        {
            text = text.Replace(mention.Key, FormatMention(mention));
        }

        return text;
    }

    private static string FormatMention(ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent mention)
    {
        static bool Real(string? v) => !string.IsNullOrEmpty(v) && v != "all";

        var name = string.IsNullOrEmpty(mention.Name) ? "未知用户" : mention.Name;

        var id = mention.Id;
        var parts = new List<string>();
        if (Real(id?.OpenId)) parts.Add($"open_id:{id!.OpenId}");
        if (Real(id?.UserId)) parts.Add($"user_id:{id!.UserId}");
        if (Real(id?.UnionId)) parts.Add($"union_id:{id!.UnionId}");

        return parts.Count == 0 ? $"@{name}" : $"@{name}({string.Join(", ", parts)})";
    }
```

> 文件已有 `using FeishuNetSdk.Im.Events;`(可解析 `ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent`)。`System.Linq`、`System.Collections.Generic` 由 `<ImplicitUsings>enable</ImplicitUsings>` 隐式引入,无需补 using。实现里**不**出现 `UserIdSuffix` 类型名,故无需 `using FeishuNetSdk.Core;`。

- [ ] **Step 4: 运行测试,确认全部通过**

Run:
```bash
dotnet test test/FeishuAdaptor.Tests/FeishuAdaptor.Tests.csproj --filter "FullyQualifiedName~ResolveMentionsTests"
```
Expected: `Passed: 9`(9 个用例全绿)。

- [ ] **Step 5: 提交**

```bash
git add test/FeishuAdaptor.Tests/ResolveMentionsTests.cs demo/FeishuAdaptor/EventHandlers/ImMessageReceiveEventHandler.cs
git commit -m "$(cat <<'EOF'
✨ 飞书 @提及内联注入:ResolveMentions 把 @_user_N 替换为 @名字(open_id/user_id/union_id)

AgentLauncher 新增 internal static ResolveMentions + FormatMention,text 分支待接线;
9 个纯函数单测覆盖单/多提及、外部用户、@所有人、null/空、占位符无对应、缺名、≥10 子串安全。

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: 把 ResolveMentions 接入 HandleMessage 的 text 分支

**Files:**
- Modify: `demo/FeishuAdaptor/EventHandlers/ImMessageReceiveEventHandler.cs`(`HandleMessage` 的 `"text"` case,约 223-229 行)

- [ ] **Step 1: 修改 text 分支**

把:

```csharp
            case "text":
            {
                var doc = JsonDocument.Parse(messageContent);
                var text = doc.RootElement.GetProperty("text").GetString()!;
                result = text;
                break;
            }
```

改为:

```csharp
            case "text":
            {
                var doc = JsonDocument.Parse(messageContent);
                var text = doc.RootElement.GetProperty("text").GetString()!;
                result = ResolveMentions(text, input.Event.Message?.Mentions);
                break;
            }
```

> 仅此一处改动。`input.Event.Message?.Mentions` 类型为 `MentionEvent[]?`,可空由 `ResolveMentions` 内部处理;无 mentions 时输出与改动前完全一致(向后兼容)。`p2p` 过滤(line 25)不动。

- [ ] **Step 2: 构建 FeishuAdaptor,确认编译通过**

Run:
```bash
dotnet build demo/FeishuAdaptor/FeishuAdaptor.csproj
```
Expected: `Build succeeded`(0 error)。

- [ ] **Step 3: 跑全量测试,确认无回归**

Run:
```bash
dotnet test test/FeishuAdaptor.Tests/FeishuAdaptor.Tests.csproj
```
Expected: 全部通过(含既有用例与新增 9 个)。

- [ ] **Step 4: 提交**

```bash
git add demo/FeishuAdaptor/EventHandlers/ImMessageReceiveEventHandler.cs
git commit -m "$(cat <<'EOF'
✨ HandleMessage text 分支接入 ResolveMentions:@提及信息内联进正文

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## 自检清单(实现完成后人工核对)

- 无 mentions 的 p2p 文本消息输出与改动前**逐字一致**(向后兼容)。
- `p2p` 过滤(line 25)**未改动**。
- 未引入额外飞书 API 调用;仅用 payload 字段。
- 未改动 `post`/`file`/其他分支与卡片会话、文件下载逻辑。
