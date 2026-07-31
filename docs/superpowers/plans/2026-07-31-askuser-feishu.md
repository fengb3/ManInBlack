# AskUser 飞书提问工具 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 FeishuAdaptor 内新增 `[AiTool] AskUserAsync`，agent 调用后向飞书用户发交互卡片（单选按钮 / 多选下拉+提交），阻塞等待用户点选，把选择结果作为工具返回值交回 LLM。

**Architecture:** 工具发卡后在单例 `PendingAskRegistry` 里挂一个 `TaskCompletionSource`（key= requestId，requestId 嵌入卡片交互元素的 `CallbackBehavior.Value`）；新增 `CardActionCallbackHandler`（FeishuNetSdk `ICallbackHandler<...CardActionTriggerEventBodyDto...>`）接收按钮/提交回调，按 requestId 解决 TCS，工具解开阻塞返回。超时/agent 取消经 linked token 兜底。全部新代码在 `demo/FeishuAdaptor/`，不动 `src/ManInBlack.AI` 核心与源生成器（复杂参数能力已就绪）。

**Tech Stack:** .NET 10、`Microsoft.Extensions.AI`、FeishuNetSdk 4.2.4（`CardActionTriggerEventBodyDto` / `ICallbackHandler<,,>`）、xunit + NSubstitute（FeishuAdaptor.Tests 允许 NSubstitute）、源生成器 `[AiTool]`。

**关键事实（已核实）：**
- `CardService.CreateAsync(Card, ct)→cardId`（`CardService.cs:18`）、`SendMessageAsync(cardId, receiveIdType, receiveId, ct)`（`CardService.cs:36`）。
- `ButtonElement.Behaviors: List<ActionBehavior>`，`CallbackBehavior.Value: object?`（`CardElement.cs:178-183`）。`FormElement{Name,Elements}`（`ContainerElements.cs:154`），`MultiSelectStaticElement{Options,Name}`（`InteractiveElements.cs:277`）。
- 卡片回调：`FeishuNetSdk.CallbackEvents.CardActionTriggerEventBodyDto`，`Action.Value: Dictionary<string,object>`、`Action.FormValue: Dictionary<string,object>`、`Operator.UserId`。
- 回调处理器接口：`ICallbackHandler<EventV2Dto<CardActionTriggerEventBodyDto>, CardActionTriggerEventBodyDto, CardActionTriggerResponseDto>`，方法 `Task<CardActionTriggerResponseDto> ExecuteAsync(EventV2Dto<CardActionTriggerEventBodyDto> input, CancellationToken ct)`（`EventV2Dto<T>.Event` 有公开 setter，可测试构造）。`AddFeishuNetSdk(...)` 自动扫描程序集发现 `IEventHandler`/`ICallbackHandler`（参考 `ImMessageReceiveEventHandler` 无 `[ServiceRegister]`、无手动注册）→ 新 handler 无需手动注册。
- 源生成器把 `AddToolHandlers()` 生成到 `build_property.RootNamespace`（FeishuAdaptor 的 = `FeishuAdaptor`，`Program.cs:4` 已 `using FeishuAdaptor;`）。
- `AgentFactory.RunAsync` 在 agent scope 内设 `agentContext.RootUserId`（`AgentFactory.cs:178`）= 飞书 user_id；`agentContext.CancellationToken`（`AgentContext.cs:79`）。
- `[AiTool]` 类须 `partial`；SG 的对象属性 `required` 仅看可空性（`ToolCallerGenerator.cs:591-593`）。
- 测试工程 `test/FeishuAdaptor.Tests/`，引用 FeishuAdaptor + NSubstitute。

**约定：** 注释/文档用中文；commit 用 gitmoji 前缀，**禁止** `Co-authored-by` 尾注；测试用 NSubstitute（本工程例外允许）。

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `demo/FeishuAdaptor/Tools/AskUserOption.cs` | 选项 record（Label/Description/Value）|
| `demo/FeishuAdaptor/Tools/PendingAskRegistry.cs` | 单例注册表 + `PendingAsk` + `AskUserResult` |
| `demo/FeishuAdaptor/FeishuCard/AskUserCardBuilder.cs` | 按单/多选构建 `Card` |
| `demo/FeishuAdaptor/Tools/AskUserTool.cs` | `[AiTool] AskUserAsync` |
| `demo/FeishuAdaptor/EventHandlers/CardActionCallbackHandler.cs` | 卡片回调 → 解决 registry |
| `demo/FeishuAdaptor/Program.cs` | 补 `services.AddToolHandlers();` |
| `test/FeishuAdaptor.Tests/CardServiceTests.cs`、`MergeCardViewTests.cs` | 修复现存构造参数缺失 |
| `test/FeishuAdaptor.Tests/` 新增 4 个测试文件 | 各组件单测 |
| `docs/tools-guide.md` | AskUser 用法文档 |

---

## Task 1: 修复 FeishuAdaptor.Tests 现存编译错误

**Files:**
- Modify: `test/FeishuAdaptor.Tests/CardServiceTests.cs:25`
- Modify: `test/FeishuAdaptor.Tests/MergeCardViewTests.cs:28`

`CardService` 构造为 `(IFeishuTenantApi api, CardApiLimiter limiter, ILogger<CardService> logger)`，两个测试只传了 2 个参数。

- [ ] **Step 1: 修 CardServiceTests.cs**

在 `test/FeishuAdaptor.Tests/CardServiceTests.cs` 顶部 `using` 区加（若无）：

```csharp
using Microsoft.Extensions.Logging;
```

把第 25 行：

```csharp
        _sut = new CardService(_api, _limiter);
```

改为：

```csharp
        _sut = new CardService(_api, _limiter, Substitute.For<ILogger<CardService>>());
```

- [ ] **Step 2: 修 MergeCardViewTests.cs**

`test/FeishuAdaptor.Tests/MergeCardViewTests.cs` 已有 `using Microsoft.Extensions.Logging;`。把第 28 行：

```csharp
        _cardService = new CardService(_api, new CardApiLimiter());
```

改为：

```csharp
        _cardService = new CardService(_api, new CardApiLimiter(), Substitute.For<ILogger<CardService>>());
```

- [ ] **Step 3: 验证编译通过**

Run: `dotnet build test/FeishuAdaptor.Tests`
Expected: Build SUCCESS（之前报 CS7036 参数缺失，现应消失）。

- [ ] **Step 4: 提交**

```bash
git add test/FeishuAdaptor.Tests/CardServiceTests.cs test/FeishuAdaptor.Tests/MergeCardViewTests.cs
git commit -m "🐛 [FeishuAdaptor.Tests] 修复 CardService 构造缺少 logger 参数"
```

---

## Task 2: AskUserOption + PendingAskRegistry（TDD）

**Files:**
- Create: `demo/FeishuAdaptor/Tools/AskUserOption.cs`
- Create: `demo/FeishuAdaptor/Tools/PendingAskRegistry.cs`
- Test: `test/FeishuAdaptor.Tests/PendingAskRegistryTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `test/FeishuAdaptor.Tests/PendingAskRegistryTests.cs`：

```csharp
using FeishuAdaptor.Tools;
using Xunit;

namespace FeishuAdaptor.Tests;

public class PendingAskRegistryTests
{
    private static PendingAsk NewAsk(out TaskCompletionSource<AskUserResult> tcs)
    {
        tcs = new TaskCompletionSource<AskUserResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        return new PendingAsk
        {
            Tcs = tcs,
            MultiSelect = false,
            OptionsByValue = new Dictionary<string, AskUserOption>(),
            AskedUserId = "u1",
        };
    }

    [Fact]
    public void Register_And_TryGet()
    {
        var reg = new PendingAskRegistry();
        var ask = NewAsk(out _);
        reg.Register("r1", ask);
        Assert.True(reg.TryGet("r1", out var got));
        Assert.Same(ask, got);
    }

    [Fact]
    public void Resolve_Completes_Tcs_And_Removes_Entry()
    {
        var reg = new PendingAskRegistry();
        var ask = NewAsk(out var tcs);
        reg.Register("r1", ask);

        Assert.True(reg.Resolve("r1", new AskUserResult(new[] { "yes" })));
        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.False(reg.TryGet("r1", out _));
    }

    [Fact]
    public async Task Resolve_Is_Idempotent_On_Duplicate()
    {
        var reg = new PendingAskRegistry();
        var ask = NewAsk(out var tcs);
        reg.Register("r1", ask);

        Assert.True(reg.Resolve("r1", new AskUserResult(new[] { "yes" })));
        // 第二次：条目已移除，返回 false，TCS 保持第一次的值
        Assert.False(reg.Resolve("r1", new AskUserResult(new[] { "no" })));

        var completed = await tcs.Task;
        Assert.Equal(new[] { "yes" }, completed.SelectedValues);
    }

    [Fact]
    public void Resolve_Unknown_RequestId_Returns_False()
    {
        var reg = new PendingAskRegistry();
        Assert.False(reg.Resolve("nope", new AskUserResult(Array.Empty<string>())));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test test/FeishuAdaptor.Tests --filter "FullyQualifiedName~PendingAskRegistryTests"`
Expected: FAIL — 类型 `AskUserOption`/`PendingAsk`/`AskUserResult`/`PendingAskRegistry` 未定义（CS0246）。

- [ ] **Step 3: 实现 AskUserOption**

创建 `demo/FeishuAdaptor/Tools/AskUserOption.cs`：

```csharp
namespace FeishuAdaptor.Tools;

/// <summary>
/// AskUser 工具的一个可选项。源生成器按公共可读属性生成 schema：
/// <see cref="Label"/> 非可空 → schema required；<see cref="Description"/>/<see cref="Value"/> 可空 → 可选。
/// </summary>
public record AskUserOption
{
    /// <summary>选项展示文案（必填，显示在按钮/选项上）。</summary>
    public string Label { get; set; } = "";

    /// <summary>辅助说明（可选）。</summary>
    public string? Description { get; set; }

    /// <summary>回传值（可选）；为空时回退为 <see cref="Label"/>。</summary>
    public string? Value { get; set; }

    public AskUserOption() { }

    public AskUserOption(string label)
    {
        Label = label;
        Value = label;
    }
}
```

- [ ] **Step 4: 实现 PendingAskRegistry**

创建 `demo/FeishuAdaptor/Tools/PendingAskRegistry.cs`：

```csharp
using System.Collections.Concurrent;
using ManInBlack.AI.Abstraction.Attributes;

namespace FeishuAdaptor.Tools;

/// <summary>用户选择结果（一个或多个选项的 Value）。</summary>
public record AskUserResult(string[] SelectedValues);

/// <summary>
/// 一次挂起的提问：工具发卡后阻塞在此 <see cref="Tcs"/>，等卡片回调 handler 解决。
/// </summary>
public sealed class PendingAsk
{
    public required TaskCompletionSource<AskUserResult> Tcs { get; init; }
    public required bool MultiSelect { get; init; }
    public required IReadOnlyDictionary<string, AskUserOption> OptionsByValue { get; init; }
    public required string AskedUserId { get; init; }
}

/// <summary>
/// 进程级单例：按 requestId 关联「挂起的提问」与「卡片回调」。工具（agent scope）
/// 与回调 handler（独立 webhook scope）跨 scope 靠此单例打通。
/// </summary>
[ServiceRegister.Singleton]
public class PendingAskRegistry
{
    private readonly ConcurrentDictionary<string, PendingAsk> _pending = new();

    public void Register(string requestId, PendingAsk ask) => _pending[requestId] = ask;

    public bool TryGet(string requestId, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out PendingAsk ask)
        => _pending.TryGetValue(requestId, out ask);

    public bool TryRemove(string requestId, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out PendingAsk ask)
        => _pending.TryRemove(requestId, out ask);

    /// <summary>解决一次提问：TrySetResult 成功后移除条目。对已解决/未知 requestId 幂等（返回 false）。</summary>
    public bool Resolve(string requestId, AskUserResult result)
    {
        if (!_pending.TryGetValue(requestId, out var ask))
            return false;
        if (!ask.Tcs.TrySetResult(result))
            return false;
        _pending.TryRemove(requestId, out _);
        return true;
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test test/FeishuAdaptor.Tests --filter "FullyQualifiedName~PendingAskRegistryTests"`
Expected: PASS（4 个用例全绿）。

- [ ] **Step 6: 提交**

```bash
git add demo/FeishuAdaptor/Tools/AskUserOption.cs demo/FeishuAdaptor/Tools/PendingAskRegistry.cs test/FeishuAdaptor.Tests/PendingAskRegistryTests.cs
git commit -m "✨ [FeishuAdaptor] AskUser 挂起提问注册表 PendingAskRegistry"
```

---

## Task 3: AskUserCardBuilder（TDD）

**Files:**
- Create: `demo/FeishuAdaptor/FeishuCard/AskUserCardBuilder.cs`
- Test: `test/FeishuAdaptor.Tests/AskUserCardBuilderTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `test/FeishuAdaptor.Tests/AskUserCardBuilderTests.cs`：

```csharp
using FeishuAdaptor.FeishuCard;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Tools;
using Xunit;

namespace FeishuAdaptor.Tests;

public class AskUserCardBuilderTests
{
    private static readonly AskUserOption[] Opts =
    {
        new("是") { Value = "yes" },
        new("否") { Value = "no" },
    };

    [Fact]
    public void Single_Select_Builds_Buttons_With_RequestId_And_Option()
    {
        var card = AskUserCardBuilder.Build("继续吗？", Opts, multiSelect: false, "rid123");
        var json = card.ToJson();

        Assert.Contains("\"requestId\":\"rid123\"", json);
        Assert.Contains("\"option\":\"yes\"", json);
        Assert.Contains("\"option\":\"no\"", json);
        Assert.Contains("\"tag\":\"button\"", json);
        // 单选不应出现表单
        Assert.DoesNotContain("\"tag\":\"form\"", json);
    }

    [Fact]
    public void Multi_Select_Builds_Form_With_MultiSelect_And_Submit()
    {
        var card = AskUserCardBuilder.Build("选哪些？", Opts, multiSelect: true, "rid456");
        var json = card.ToJson();

        Assert.Contains("\"tag\":\"form\"", json);
        Assert.Contains("\"tag\":\"multi_select_static\"", json);
        Assert.Contains("\"name\":\"opts\"", json);
        Assert.Contains("\"submit\"", json); // 提交按钮 FormActionType
        Assert.Contains("\"requestId\":\"rid456\"", json); // 提交键 Value 带 requestId
        // 选项回传值进入 multi_select 的 options
        Assert.Contains("\"yes\"", json);
        Assert.Contains("\"no\"", json);
    }

    [Fact]
    public void Header_Contains_Question_Or_Title()
    {
        var card = AskUserCardBuilder.Build("标题问题", Opts, false, "r");
        var json = card.ToJson();
        Assert.Contains("标题问题", json);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test test/FeishuAdaptor.Tests --filter "FullyQualifiedName~AskUserCardBuilderTests"`
Expected: FAIL — `AskUserCardBuilder` 未定义。

- [ ] **Step 3: 实现 AskUserCardBuilder**

创建 `demo/FeishuAdaptor/FeishuCard/AskUserCardBuilder.cs`：

```csharp
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Tools;

namespace FeishuAdaptor.FeishuCard;

/// <summary>
/// 构造 AskUser 的交互卡片。
/// 单选：问题 markdown + 每个选项一个按钮（CallbackBehavior.Value 带 requestId + option）。
/// 多选：form 容器内含问题 markdown + multi_select_static(name=opts) + 提交按钮（Value 带 requestId）。
/// </summary>
public static class AskUserCardBuilder
{
    public static Card Build(string question, IReadOnlyList<AskUserOption> options, bool multiSelect, string requestId)
        => multiSelect ? BuildMulti(question, options, requestId) : BuildSingle(question, options, requestId);

    private static Card MakeCard(string headerText, params CardElement[] bodyElements) => new()
    {
        Header = new CardHeader { Title = new TextElement("需要你的选择") },
        Body = new CardBody { Elements = bodyElements.ToList() },
    };

    private static MarkdownElement QuestionMarkdown(string question) => new()
    {
        Content = question,
        TextAlign = "left",
    };

    private static Card BuildSingle(string question, IReadOnlyList<AskUserOption> options, string requestId)
    {
        var elements = new List<CardElement> { QuestionMarkdown(question) };
        for (var i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            elements.Add(new ButtonElement
            {
                Text = new TextElement(opt.Label),
                Type = i == 0 ? "primary" : "default",
                Behaviors =
                {
                    new CallbackBehavior
                    {
                        Value = new Dictionary<string, object>
                        {
                            ["requestId"] = requestId,
                            ["option"] = opt.Value ?? opt.Label,
                        },
                    },
                },
            });
        }
        return MakeCard(question, elements.ToArray());
    }

    private static Card BuildMulti(string question, IReadOnlyList<AskUserOption> options, string requestId)
    {
        var select = new MultiSelectStaticElement
        {
            Name = "opts",
            Placeholder = new TextElement("请选择"),
            Options = options
                .Select(o => new SelectOption
                {
                    Text = new TextElement(o.Label),
                    Value = o.Value ?? o.Label,
                })
                .ToList(),
        };
        var submit = new ButtonElement
        {
            Text = new TextElement("提交"),
            Type = "primary",
            FormActionType = "submit",
            Behaviors =
            {
                new CallbackBehavior
                {
                    Value = new Dictionary<string, object> { ["requestId"] = requestId },
                },
            },
        };
        var form = new FormElement
        {
            Name = "askUserForm",
            Elements = { QuestionMarkdown(question), select, submit },
        };
        return MakeCard(question, form);
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test test/FeishuAdaptor.Tests --filter "FullyQualifiedName~AskUserCardBuilderTests"`
Expected: PASS（3 个用例全绿）。若某断言失败，对照 `card.ToJson()` 实际输出微调断言或实现（snake_case 序列化：`tag`/`behaviors`/`value`/`form_action_type`）。

- [ ] **Step 5: 提交**

```bash
git add demo/FeishuAdaptor/FeishuCard/AskUserCardBuilder.cs test/FeishuAdaptor.Tests/AskUserCardBuilderTests.cs
git commit -m "✨ [FeishuAdaptor] AskUser 交互卡片构建器 AskUserCardBuilder"
```

---

## Task 4: AskUserTool（TDD）

**Files:**
- Create: `demo/FeishuAdaptor/Tools/AskUserTool.cs`
- Test: `test/FeishuAdaptor.Tests/AskUserToolTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `test/FeishuAdaptor.Tests/AskUserToolTests.cs`：

```csharp
using FeishuAdaptor.FeishuCard;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Tools;
using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.RegularExpressions;
using Xunit;

namespace FeishuAdaptor.Tests;

public class AskUserToolTests
{
    private static AskUserOption[] Opts => new[]
    {
        new AskUserOption("是") { Value = "yes" },
        new AskUserOption("否") { Value = "no" },
    };

    private static (AskUserTool tool, CardService card, PendingAskRegistry reg, AgentContext ctx) MakeTool(string userId = "u1")
    {
        var card = Substitute.For<CardService>(default!, default!, default!);
        var reg = new PendingAskRegistry();
        var ctx = new AgentContext(Substitute.For<IServiceProvider>()) { RootUserId = userId };
        var tool = new AskUserTool(card, reg, ctx, Substitute.For<ILogger<AskUserTool>>());
        return (tool, card, reg, ctx);
    }

    private static string? ExtractRequestId(Card? card)
    {
        if (card is null) return null;
        var m = Regex.Match(card.ToJson(), "\"requestId\":\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    [Fact]
    public async Task Empty_Options_Returns_Failure_Without_Sending_Card()
    {
        var (tool, card, _, _) = MakeTool();
        var ret = await tool.AskUserAsync("q", new List<AskUserOption>(), false, 1);
        Assert.Contains("未提供可选项", ret);
        await card.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Resolved_Returns_Selected_Label()
    {
        var (tool, card, reg, _) = MakeTool();
        Card? captured = null;
        card.CreateAsync(Arg.Any<Card>(), Arg.Any<CancellationToken>()).Returns("card-1");
        card.When(x => x.CreateAsync(Arg.Any<Card>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<Card>());

        var task = Task.Run(() => tool.AskUserAsync("继续吗", new List<AskUserOption>(Opts), false, 30));
        // 工具先 Register 再 CreateAsync；captured 被赋值时 requestId 必已注册
        while (captured is null) await Task.Delay(10);
        reg.Resolve(ExtractRequestId(captured)!, new AskUserResult(new[] { "yes" }));

        var ret = await task;
        Assert.Equal("用户选择了：是", ret);
    }

    [Fact]
    public async Task Timeout_Returns_Timeout_Message()
    {
        var (tool, card, _, _) = MakeTool();
        card.CreateAsync(Arg.Any<Card>(), Arg.Any<CancellationToken>()).Returns("card-1");
        var ret = await tool.AskUserAsync("q", new List<AskUserOption>(Opts), false, 0);
        Assert.Contains("超时", ret);
    }

    [Fact]
    public async Task Agent_Cancelled_Returns_Cancel_Message()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var card = Substitute.For<CardService>(default!, default!, default!);
        var reg = new PendingAskRegistry();
        var ctx = new AgentContext(Substitute.For<IServiceProvider>())
        { RootUserId = "u1", CancellationToken = cts.Token };
        var tool = new AskUserTool(card, reg, ctx, Substitute.For<ILogger<AskUserTool>>());
        card.CreateAsync(Arg.Any<Card>(), Arg.Any<CancellationToken>()).Returns("card-1");

        var ret = await tool.AskUserAsync("q", new List<AskUserOption>(Opts), false, 30);
        Assert.Contains("取消", ret);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test test/FeishuAdaptor.Tests --filter "FullyQualifiedName~AskUserToolTests"`
Expected: FAIL — `AskUserTool` 未定义。

- [ ] **Step 3: 实现 AskUserTool**

创建 `demo/FeishuAdaptor/Tools/AskUserTool.cs`：

```csharp
using FeishuAdaptor.FeishuCard;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.Logging;

namespace FeishuAdaptor.Tools;

/// <summary>向飞书用户提问并阻塞等待其卡片选择的工具。</summary>
[ServiceRegister.Scoped]
public partial class AskUserTool(
    CardService cardService,
    PendingAskRegistry registry,
    AgentContext agentContext,
    ILogger<AskUserTool> logger)
{
    /// <summary>向当前飞书用户发送一张单选/多选卡片，阻塞等待用户选择后返回结果。</summary>
    /// <param name="question">要问用户的问题文本。</param>
    /// <param name="options">可选项列表。</param>
    /// <param name="multiSelect">是否允许多选，默认 false（单选）。</param>
    /// <param name="timeoutSeconds">等待超时秒数，默认 300；超时自动结束。</param>
    /// <returns>用户的选择（如「用户选择了：是」），或超时/取消/错误提示。</returns>
    [AiTool]
    public async Task<string> AskUserAsync(
        string question,
        List<AskUserOption> options,
        bool multiSelect = false,
        int timeoutSeconds = 300)
    {
        if (options is null || options.Count == 0)
            return "提问失败：未提供可选项";

        var userId = agentContext.RootUserId;
        var requestId = Guid.NewGuid().ToString("N");
        var card = AskUserCardBuilder.Build(question, options, multiSelect, requestId);

        var optionsByValue = options.ToDictionary(o => o.Value ?? o.Label, o => o);
        var tcs = new TaskCompletionSource<AskUserResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 先注册再建卡：便于回调在卡片送达后即可命中；异常时 finally 兜底移除。
        registry.Register(requestId, new PendingAsk
        {
            Tcs = tcs,
            MultiSelect = multiSelect,
            OptionsByValue = optionsByValue,
            AskedUserId = userId,
        });

        try
        {
            var cardId = await cardService.CreateAsync(card, agentContext.CancellationToken);
            await cardService.SendMessageAsync(cardId, "user_id", userId, agentContext.CancellationToken);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                agentContext.CancellationToken, timeoutCts.Token);

            var delay = Task.Delay(Timeout.Infinite, linkedCts.Token);
            var done = await Task.WhenAny(tcs.Task, delay);

            if (done == tcs.Task && tcs.Task.IsCompletedSuccessfully)
            {
                var result = await tcs.Task;
                return FormatSelection(optionsByValue, result);
            }

            // 未在窗口内解决：区分 agent 取消 vs 超时
            return agentContext.CancellationToken.IsCancellationRequested
                ? "提问已被取消（用户发起了新对话或会话结束）"
                : $"用户未在 {timeoutSeconds} 秒内作答（已超时）";
        }
        catch (OperationCanceledException)
        {
            return agentContext.CancellationToken.IsCancellationRequested
                ? "提问已被取消（用户发起了新对话或会话结束）"
                : $"用户未在 {timeoutSeconds} 秒内作答（已超时）";
        }
        finally
        {
            registry.TryRemove(requestId, out _);
        }
    }

    private static string FormatSelection(IReadOnlyDictionary<string, AskUserOption> optionsByValue, AskUserResult result)
    {
        var labels = result.SelectedValues
            .Select(v => optionsByValue.TryGetValue(v, out var o) ? o.Label : v);
        return "用户选择了：" + string.Join("、", labels);
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test test/FeishuAdaptor.Tests --filter "FullyQualifiedName~AskUserToolTests"`
Expected: PASS（4 个用例全绿）。

- [ ] **Step 5: 提交**

```bash
git add demo/FeishuAdaptor/Tools/AskUserTool.cs test/FeishuAdaptor.Tests/AskUserToolTests.cs
git commit -m "✨ [FeishuAdaptor] AskUser 提问工具：发卡→阻塞→回选择结果"
```

---

## Task 5: CardActionCallbackHandler（TDD）

**Files:**
- Create: `demo/FeishuAdaptor/EventHandlers/CardActionCallbackHandler.cs`
- Test: `test/FeishuAdaptor.Tests/CardActionCallbackHandlerTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `test/FeishuAdaptor.Tests/CardActionCallbackHandlerTests.cs`：

```csharp
using FeishuAdaptor.EventHandlers;
using FeishuAdaptor.Tools;
using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FeishuAdaptor.Tests;

public class CardActionCallbackHandlerTests
{
    private static EventV2Dto<CardActionTriggerEventBodyDto> SingleSelectInput(string requestId, string option)
    {
        var body = new CardActionTriggerEventBodyDto
        {
            Action = new CardActionTriggerEventBodyDto.ActionSuffix
            {
                Value = new Dictionary<string, object> { ["requestId"] = requestId, ["option"] = option },
            },
        };
        return new EventV2Dto<CardActionTriggerEventBodyDto> { Event = body };
    }

    private static EventV2Dto<CardActionTriggerEventBodyDto> MultiSelectInput(string requestId, string[] selected)
    {
        var body = new CardActionTriggerEventBodyDto
        {
            Action = new CardActionTriggerEventBodyDto.ActionSuffix
            {
                Value = new Dictionary<string, object> { ["requestId"] = requestId },
                FormValue = new Dictionary<string, object> { ["opts"] = selected },
            },
        };
        return new EventV2Dto<CardActionTriggerEventBodyDto> { Event = body };
    }

    private static PendingAskRegistry RegistryWith(string requestId, out TaskCompletionSource<AskUserResult> tcs)
    {
        var reg = new PendingAskRegistry();
        tcs = new TaskCompletionSource<AskUserResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        reg.Register(requestId, new PendingAsk
        {
            Tcs = tcs,
            MultiSelect = false,
            OptionsByValue = new Dictionary<string, AskUserOption>(),
            AskedUserId = "u1",
        });
        return reg;
    }

    [Fact]
    public async Task Single_Select_Resolves_With_Option_Value()
    {
        var reg = RegistryWith("rid1", out var tcs);
        var handler = new CardActionCallbackHandler(reg, Substitute.For<ILogger<CardActionCallbackHandler>>());

        var resp = await handler.ExecuteAsync(SingleSelectInput("rid1", "yes"), CancellationToken.None);

        Assert.NotNull(resp);
        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.Equal(new[] { "yes" }, (await tcs.Task).SelectedValues);
    }

    [Fact]
    public async Task Multi_Select_Resolves_With_Form_Values()
    {
        var reg = RegistryWith("rid2", out var tcs);
        var handler = new CardActionCallbackHandler(reg, Substitute.For<ILogger<CardActionCallbackHandler>>());

        var resp = await handler.ExecuteAsync(MultiSelectInput("rid2", new[] { "a", "b" }), CancellationToken.None);

        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.Equal(new[] { "a", "b" }, (await tcs.Task).SelectedValues);
    }

    [Fact]
    public async Task Unknown_RequestId_Does_Not_Throw_And_Leaves_Registry()
    {
        var reg = new PendingAskRegistry();
        var handler = new CardActionCallbackHandler(reg, Substitute.For<ILogger<CardActionCallbackHandler>>());

        var resp = await handler.ExecuteAsync(SingleSelectInput("unknown", "x"), CancellationToken.None);

        Assert.NotNull(resp);
        // 不应抛异常；registry 仍空
        Assert.False(reg.Resolve("unknown", new AskUserResult(Array.Empty<string>())));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test test/FeishuAdaptor.Tests --filter "FullyQualifiedName~CardActionCallbackHandlerTests"`
Expected: FAIL — `CardActionCallbackHandler` 未定义。

- [ ] **Step 3: 实现 CardActionCallbackHandler**

创建 `demo/FeishuAdaptor/EventHandlers/CardActionCallbackHandler.cs`：

```csharp
using FeishuAdaptor.Tools;
using FeishuNetSdk.CallbackEvents;
using FeishuNetSdk.Services;
using Microsoft.Extensions.Logging;

namespace FeishuAdaptor.EventHandlers;

/// <summary>
/// 飞书卡片回传交互处理器：用户点击 AskUser 卡片的按钮/提交时触发，
/// 按 requestId 在 <see cref="PendingAskRegistry"/> 中解决挂起的提问。
/// 由 <c>AddFeishuNetSdk(...)</c> 自动发现（同 <see cref="ImMessageReceiveEventHandler"/>）。
/// </summary>
public class CardActionCallbackHandler(
    PendingAskRegistry registry,
    ILogger<CardActionCallbackHandler> logger)
    : ICallbackHandler<EventV2Dto<CardActionTriggerEventBodyDto>, CardActionTriggerEventBodyDto, CardActionTriggerResponseDto>
{
    public Task<CardActionTriggerResponseDto> ExecuteAsync(
        EventV2Dto<CardActionTriggerEventBodyDto> input,
        CancellationToken cancellationToken)
    {
        var body = input.Event;
        var action = body?.Action;
        var value = action?.Value;

        if (value is null || !value.TryGetValue("requestId", out var ridObj) || ridObj is not string requestId)
        {
            logger.LogDebug("收到无 requestId 的卡片回调，忽略");
            return Task.FromResult(Toast("无效的提问回调"));
        }

        if (!registry.TryGet(requestId, out var ask) || ask is null)
        {
            logger.LogDebug("卡片回调 requestId={RequestId} 无对应挂起提问（已过期/已回答）", requestId);
            return Task.FromResult(Toast("问题已过期或已回答"));
        }

        var selected = CollectSelected(action!, ask.MultiSelect);
        registry.Resolve(requestId, new AskUserResult(selected));
        return Task.FromResult(Toast("已收到你的选择"));
    }

    private static string[] CollectSelected(CardActionTriggerEventBodyDto.ActionSuffix action, bool multiSelect)
    {
        if (multiSelect)
        {
            if (action.FormValue is not null && action.FormValue.TryGetValue("opts", out var opts))
                return ToStringArray(opts);
            return action.Options ?? Array.Empty<string>();
        }

        if (action.Value is not null && action.Value.TryGetValue("option", out var opt))
            return new[] { opt?.ToString() ?? string.Empty };
        return Array.Empty<string>();
    }

    private static string[] ToStringArray(object? opts) => opts switch
    {
        null => Array.Empty<string>(),
        string[] arr => arr,
        IEnumerable<object> list => list.Select(o => o?.ToString() ?? string.Empty).ToArray(),
        System.Text.Json.JsonElement je => je.ValueKind == System.Text.Json.JsonValueKind.Array
            ? je.EnumerateArray().Select(e => e.ToString()).ToArray()
            : new[] { je.ToString() },
        _ => new[] { opts.ToString() ?? string.Empty },
    };

    private static CardActionTriggerResponseDto Toast(string msg) => new()
    {
        // ToastSuffix.Content 为直接字符串属性；Type(ToastType?) 可选，省略。
        Toast = new CardActionTriggerResponseDto.ToastSuffix { Content = msg },
    };
}
```

> **实现期注意：** `ToastSuffix.Content` 为直接字符串属性（已对照 FeishuNetSdk 4.2.4）。`CollectSelected` 的多选分支以 `FormValue["opts"]` 为主、`Options` 作兜底，`ToStringArray` 防御性处理 `string[]`/`IEnumerable<object>`/`JsonElement`；这是唯一需要对照真实回调载荷确认的点（见 Task 8 部署前置 Step 4）。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test test/FeishuAdaptor.Tests --filter "FullyQualifiedName~CardActionCallbackHandlerTests"`
Expected: PASS（3 个用例全绿）。若 `Toast` 构造编译失败，按上面注意点对照 SDK 调整 `Toast(...)` 实现（不影响测试对「resolve 行为」的断言）。

- [ ] **Step 5: 提交**

```bash
git add demo/FeishuAdaptor/EventHandlers/CardActionCallbackHandler.cs test/FeishuAdaptor.Tests/CardActionCallbackHandlerTests.cs
git commit -m "✨ [FeishuAdaptor] AskUser 卡片回传交互处理器 CardActionCallbackHandler"
```

---

## Task 6: Program.cs 接线

**Files:**
- Modify: `demo/FeishuAdaptor/Program.cs`（`AddManInBlack()` 链之后，约 `:78`）

- [ ] **Step 1: 注册 FeishuAdaptor 自己的工具**

在 `demo/FeishuAdaptor/Program.cs` 的 `builder.Services.AddManInBlack()...AddPipeline("sub-agent", ...);` 块（`:66-78`）之后、`builder.Services.AddAutoRegisteredServices();`（`:80`）之前，插入：

```csharp
// 注册 FeishuAdaptor 程序集内的 [AiTool]（源生成器为本程序集生成的 internal 扩展；
// AddManInBlack 内的同名调用只覆盖 ManInBlack.AI 的工具）。
builder.Services.AddToolHandlers();
```

（`Program.cs:4` 已有 `using FeishuAdaptor;`，扩展方法可见。）

- [ ] **Step 2: 构建确认源生成器生成了 AskUser handler**

Run: `dotnet build demo/FeishuAdaptor`
Expected: Build SUCCESS。无 MIB010–MIB014 诊断（`AskUserTool` 为 `partial`、参数受支持、有完整 XML 注释）。

- [ ] **Step 3: 确认生成代码含 AskUser（可选抽查）**

Run: `find demo/FeishuAdaptor/obj -name "ToolHandlers.g.cs" -exec grep -l "AskUserAsync" {} \;`
Expected: 命中一个 `ToolHandlers.g.cs` 文件，内含 `AskUserAsync` 的 handler 注册。若无，确认 `AskUserTool` 类为 `partial` 且方法有 `[AiTool]`。

- [ ] **Step 4: 提交**

```bash
git add demo/FeishuAdaptor/Program.cs
git commit -m "🔧 [FeishuAdaptor] Program 注册本程序集 [AiTool] 工具（AddToolHandlers）"
```

---

## Task 7: 文档

**Files:**
- Modify: `docs/tools-guide.md`

- [ ] **Step 1: 补 AskUser 用法**

在 `docs/tools-guide.md` 末尾追加一节：

````markdown
## AskUser（仅 FeishuAdaptor）

向当前飞书用户发送一张单选/多选卡片，**阻塞等待**用户在飞书里点选，把选择结果作为工具返回值交回 LLM。适合需要用户拍板的场景（确认、分支选择、多选收集）。

```csharp
[AiTool]
public async Task<string> AskUserAsync(
    string question,                 // 问题文本
    List<AskUserOption> options,     // 可选项：Label 必填，Description/Value 可选
    bool multiSelect = false,        // true=多选下拉+提交；false=按钮单选（点一下即返回）
    int timeoutSeconds = 300);       // 超时自动结束
```

- 单选：每个选项一张按钮，点任一按钮立即返回 `用户选择了：{label}`。
- 多选：飞书原生 `multi_select_static` + 提交按钮，点提交后返回 `用户选择了：{l1}、{l2}`。
- 超时返回 `用户未在 N 秒内作答（已超时）`；会话被取消返回 `提问已被取消…`。

**部署前置**：飞书应用后台须订阅「卡片回传交互」事件并走 webhook，按钮点击回调才能送达（否则工具必超时）。回调由 `CardActionCallbackHandler`（FeishuNetSdk `ICallbackHandler`）接收，自动发现，无需手动注册。
````

- [ ] **Step 2: 提交**

```bash
git add docs/tools-guide.md
git commit -m "📝 文档：补充 AskUser 飞书提问工具用法"
```

---

## Task 8: 全量回归

- [ ] **Step 1: 构建 FeishuAdaptor + 测试工程**

Run: `dotnet build demo/FeishuAdaptor && dotnet build test/FeishuAdaptor.Tests`
Expected: 两个均 SUCCESS。

- [ ] **Step 2: 跑 FeishuAdaptor.Tests 全量**

Run: `dotnet test test/FeishuAdaptor.Tests`
Expected: 全部 PASS（含原有 CardService/MergeCardView 测试 + 新增 4 个测试类）。

- [ ] **Step 3: 跑核心回归（确保未碰核心）**

Run: `dotnet test test/ManInBlack.AI.Tests`
Expected: 全部 PASS（本轮未改核心，应为既有绿基线）。

- [ ] **Step 4: 部署前置清单（人工/上线时）**

- [ ] 飞书应用后台「事件订阅」订阅「卡片回传交互」事件，接收方式为 webhook（`app.UseFeishuEndpoint`）。
- [ ] 真机发一条让 agent 调用 AskUser 的消息，确认卡片到达、点击后 agent 收到选择继续；确认多选提交取值正确（若 `FormValue["opts"]` 不符预期，对照真实载荷调整 `CardActionCallbackHandler.CollectSelected`）。

- [ ] **Step 5: 收尾提交（验证记录）**

```bash
git commit --allow-empty -m "✅ 回归验证：AskUser 飞书工具构建+单测全绿"
```
