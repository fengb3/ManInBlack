using FeishuAdaptor.FeishuCard;
using FeishuAdaptor.FeishuCard.CardViews;
using FeishuNetSdk;
using FeishuNetSdk.Cardkit;
using FeishuNetSdk.Im;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// MergeCardView 单元测试 — 验证合并卡的块 append、reasoning 流式累积、
/// 并行工具结果按 callId 路由、关闭流式。通过 fake IFeishuTenantApi 捕获 API 调用。
/// </summary>
public class MergeCardViewTests : IAsyncLifetime
{
    private const string CardId = "merge-card-1";

    private readonly IFeishuTenantApi _api;
    private readonly CardService _cardService;
    private MergeCardView _sut = null!;

    public MergeCardViewTests()
    {
        _api = Substitute.For<IFeishuTenantApi>();
        SetupApi(_api);
        _cardService = new CardService(_api, new CardApiLimiter(), Substitute.For<ILogger<CardService>>());
    }

    public async Task InitializeAsync()
    {
        _sut = new MergeCardView(_cardService, Substitute.For<ILogger<MergeCardView>>());
        await _sut.InitializeAsync();
        await _sut.SendToUserAsync("user_id", "u1");
        _api.ClearReceivedCalls(); // 清掉建卡/发送阶段的调用，只观察后续 op
    }

    public Task DisposeAsync()
    {
        _sut.Dispose();
        return Task.CompletedTask;
    }

    private static void SetupApi(IFeishuTenantApi api)
    {
        api.PostCardkitV1CardsAsync(
                Arg.Any<PostCardkitV1CardsBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse<PostCardkitV1CardsResponseDto>
                { Code = 0, Msg = "ok", Data = new PostCardkitV1CardsResponseDto { CardId = CardId } });

        api.PostImV1MessagesAsync(
                Arg.Any<string>(),
                Arg.Any<PostImV1MessagesBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse<PostImV1MessagesResponseDto>
                { Code = 0, Msg = "ok", Data = new PostImV1MessagesResponseDto() });

        api.PostCardkitV1CardsByCardIdElementsAsync(
                Arg.Any<string>(),
                Arg.Any<PostCardkitV1CardsByCardIdElementsBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse { Code = 0, Msg = "ok" });

        api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse { Code = 0, Msg = "ok" });

        api.PutCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse { Code = 0, Msg = "ok" });

        api.PatchCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse { Code = 0, Msg = "ok" });

        api.PutCardkitV1CardsByCardIdAsync(
                Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse { Code = 0, Msg = "ok" });

        api.PatchCardkitV1CardsByCardIdSettingsAsync(
                Arg.Any<string>(),
                Arg.Any<PatchCardkitV1CardsByCardIdSettingsBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse { Code = 0, Msg = "ok" });
    }

    [Fact]
    public async Task Reasoning_流式累积应合并为单次内容更新()
    {
        // 连续入队（同一 tick 内由消费者去重）
        _sut.EnqueueAppendReasoning();
        _sut.EnqueueUpdateReasoning("Hello");
        _sut.EnqueueUpdateReasoning(" world");

        await _sut.CloseStreamingAsync();

        // 1 次 append（单个推理折叠块）
        await _api.Received(1).PostCardkitV1CardsByCardIdElementsAsync(
            CardId,
            Arg.Any<PostCardkitV1CardsByCardIdElementsBodyDto>(),
            Arg.Any<CancellationToken>());

        // 内容更新最终应含全量累积文本（去重后最后一次）
        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            CardId, Arg.Any<string>(),
            Arg.Is<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(dto => dto.Content == "Hello world"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reasoning工具Reasoning_应在同一张卡追加三个块()
    {
        _sut.EnqueueAppendReasoning();
        _sut.EnqueueUpdateReasoning("r1");
        _sut.EnqueueAppendTool("call-1", "Read", "{\"path\":\"a.txt\"}", "");
        _sut.EnqueueAppendReasoning();
        _sut.EnqueueUpdateReasoning("r2");

        await _sut.CloseStreamingAsync();

        // 整个过程只创建过一张卡（建卡阶段），后续未再建卡
        // 清掉了建卡调用，这里应为 0
        await _api.DidNotReceive().PostCardkitV1CardsAsync(
            Arg.Any<PostCardkitV1CardsBodyDto>(), Arg.Any<CancellationToken>());

        // 3 次 append：2 个推理块 + 1 个工具块
        await _api.Received(3).PostCardkitV1CardsByCardIdElementsAsync(
            CardId,
            Arg.Any<PostCardkitV1CardsByCardIdElementsBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 并行工具_结果应按callId路由到对应面板()
    {
        _sut.EnqueueAppendTool("call-1", "Read", "{}", "");
        _sut.EnqueueAppendTool("call-2", "Write", "{}", "");
        _sut.EnqueueUpdateToolResult("call-1", "result-A", isError: false);
        _sut.EnqueueUpdateToolResult("call-2", "result-B", isError: false);

        await _sut.CloseStreamingAsync();

        // 2 次 append + 2 次整面板替换
        await _api.Received(2).PostCardkitV1CardsByCardIdElementsAsync(
            CardId,
            Arg.Any<PostCardkitV1CardsByCardIdElementsBodyDto>(),
            Arg.Any<CancellationToken>());

        // 两个工具结果各自触发一次替换，且替换后的 panel JSON 含对应结果
        var replaceCalls = _api.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "PutCardkitV1CardsByCardIdElementsByElementIdAsync")
            .ToList();
        Assert.Equal(2, replaceCalls.Count);

        var replacedJsons = replaceCalls
            .Select(c =>
            {
                var dto = (PutCardkitV1CardsByCardIdElementsByElementIdBodyDto)c.GetArguments()[2]!;
                return dto.Element;
            })
            .ToList();

        Assert.Contains(replacedJsons, j => j.Contains("result-A"));
        Assert.Contains(replacedJsons, j => j.Contains("result-B"));
        Assert.All(replacedJsons, j => Assert.DoesNotContain("⏳ 执行中...", j)); // 结果替换后不再有占位
    }

    [Fact]
    public async Task 工具块_初始面板应含参数与描述()
    {
        // 描述/参数用 ASCII，避免 JSON 序列化转义中文/emoji 后明文匹配失败
        _sut.EnqueueAppendTool("call-1", "Read", "{\"path\":\"x\"}", "read-config");

        await _sut.CloseStreamingAsync();

        // append 的元素 JSON 应含描述与参数（显示名/占位为中文会转义，这里只验证 ASCII 部分）
        var appendCalls = _api.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "PostCardkitV1CardsByCardIdElementsAsync")
            .ToList();
        Assert.Single(appendCalls);

        var dto = (PostCardkitV1CardsByCardIdElementsBodyDto)appendCalls[0].GetArguments()[1]!;
        Assert.Contains("read-config", dto.Elements);
        Assert.Contains("path", dto.Elements);
    }

    [Fact]
    public async Task CloseStreaming_应局部更新折叠大面板()
    {
        _sut.EnqueueAppendReasoning();
        _sut.EnqueueUpdateReasoning("done");

        await _sut.CloseStreamingAsync();

        // turn 结束走局部更新(更新组件属性):partial_element 含 expanded:false + 收尾标题,不再全量重建
        await _api.Received(1).PatchCardkitV1CardsByCardIdElementsByElementIdAsync(
            CardId, Arg.Any<string>(),
            Arg.Is<PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto>(dto =>
                dto.PartialElement.Contains("\"expanded\":false") &&
                dto.PartialElement.Contains("\"header\"")),
            Arg.Any<CancellationToken>());

        // 确认不再走全量重建
        await _api.DidNotReceive().PutCardkitV1CardsByCardIdAsync(
            CardId, Arg.Any<PutCardkitV1CardsByCardIdBodyDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CloseStreaming_应额外调用settings_patch退出流式()
    {
        _sut.EnqueueAppendReasoning();
        _sut.EnqueueUpdateReasoning("done");

        await _sut.CloseStreamingAsync();

        // 局部更新只改组件属性,不会动 config.streaming_mode,故需追加 settings patch
        // (PatchCardkitV1CardsByCardIdSettingsAsync)确保飞书侧真正结束流式。
        // (settings JSON 是 CardService 里硬编码的字面量,冒号后带空格。)
        await _api.Received(1).PatchCardkitV1CardsByCardIdSettingsAsync(
            CardId,
            Arg.Is<PatchCardkitV1CardsByCardIdSettingsBodyDto>(dto =>
                dto.Settings.Contains("\"streaming_mode\": false") &&
                dto.Sequence > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 多个op的sequence应单调递增发送()
    {
        _sut.EnqueueAppendReasoning();
        _sut.EnqueueUpdateReasoning("a");
        _sut.EnqueueAppendTool("call-1", "Read", "{}", "");
        _sut.EnqueueUpdateToolResult("call-1", "ok", isError: false);

        await _sut.CloseStreamingAsync();

        // 收集所有带 Sequence 的调用（append / content / replace / settings），按调用时间序
        var sequences = new List<int>();
        foreach (var call in _api.ReceivedCalls())
        {
            var args = call.GetArguments();
            foreach (var arg in args)
            {
                if (arg is PostCardkitV1CardsByCardIdElementsBodyDto d1) sequences.Add(d1.Sequence);
                else if (arg is PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto d2) sequences.Add(d2.Sequence);
                else if (arg is PutCardkitV1CardsByCardIdElementsByElementIdBodyDto d3) sequences.Add(d3.Sequence);
                else if (arg is PatchCardkitV1CardsByCardIdSettingsBodyDto d4) sequences.Add(d4.Sequence);
                else if (arg is PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto d6) sequences.Add(d6.Sequence);
                else if (arg is PutCardkitV1CardsByCardIdBodyDto d5) sequences.Add(d5.Sequence);
            }
        }

        Assert.True(sequences.Count >= 4, $"预期至少 4 个带 sequence 的调用，实际 {sequences.Count}");
        // 按发送顺序应为严格递增（消费者 OrderBy(Seq) 后串行发送）
        for (var i = 1; i < sequences.Count; i++)
            Assert.True(sequences[i] > sequences[i - 1],
                $"sequence 应单调递增：seq[{i - 1}]={sequences[i - 1]} seq[{i}]={sequences[i]}");
    }

    [Fact]
    public void Dispose_多次调用应幂等不抛()
    {
        // MergeCardView 是 tracked transient,会被 DI scope 与 FeishuCardSession 各 dispose 一次。
        // 第二次进来不应抛 ObjectDisposedException(此前 _cts.Cancel() 在已释放的 CTS 上会抛)。
        _sut.Dispose();
        try { _sut.Dispose(); }
        catch (Exception ex)
        {
            Assert.Fail($"第二次 Dispose 不应抛异常,实际抛:{ex.GetType().Name}: {ex.Message}");
        }
    }
}
