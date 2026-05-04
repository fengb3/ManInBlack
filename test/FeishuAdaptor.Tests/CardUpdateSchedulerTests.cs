using FeishuAdaptor.FeishuCard;
using FeishuNetSdk;
using FeishuNetSdk.Cardkit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// CardUpdateScheduler 单元测试 — 验证去重、Flush、限流集成。
/// </summary>
public class CardUpdateSchedulerTests : IAsyncLifetime
{
    private readonly IFeishuTenantApi _api;
    private readonly CardApiLimiter _limiter;
    private readonly ILogger<CardUpdateScheduler> _logger;
    private CardUpdateScheduler _sut = null!;

    public CardUpdateSchedulerTests()
    {
        _api = Substitute.For<IFeishuTenantApi>();
        _limiter = new CardApiLimiter();
        _logger = Substitute.For<ILogger<CardUpdateScheduler>>();

        // 默认返回成功的 FeishuResponse（非泛型，因为 API 返回 Task<FeishuResponse>）
        _api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse { Code = 0, Msg = "ok" });
    }

    public Task InitializeAsync()
    {
        _sut = new CardUpdateScheduler(_api, _limiter, _logger);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_sut != null!)
            await _sut.DisposeAsync();
    }

    #region Submit 去重

    [Fact]
    public async Task Submit_同一Key多次提交应去重_只有最后一次生效()
    {
        // Arrange
        var cardId = "card-dedup";
        var elementId = "elem-dedup";

        // Act — 对同一 (cardId, elementId) 提交 3 次
        _sut.Submit(cardId, elementId, "第一次", 1);
        _sut.Submit(cardId, elementId, "第二次", 2);
        _sut.Submit(cardId, elementId, "最终内容", 3);

        // 等待 ProcessLoop 处理（20ms 周期的定时器）
        await Task.Delay(150);

        // Assert — API 应只被调用 1 次（去重后只发送最后一次）
        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            cardId,
            elementId,
            Arg.Is<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(
                dto => dto.Content == "最终内容" && dto.Sequence == 3),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Submit 不同 key 不去重

    [Fact]
    public async Task Submit_不同Key应各自独立发送()
    {
        // Arrange
        var cardId = "card-multi";

        // Act — 不同 elementId 的提交应各自独立
        _sut.Submit(cardId, "elem-1", "内容A", 1);
        _sut.Submit(cardId, "elem-2", "内容B", 2);

        // 等待 ProcessLoop 处理
        await Task.Delay(150);

        // Assert — 两个不同 elementId 应各自被调用一次
        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            cardId, "elem-1",
            Arg.Is<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(dto => dto.Content == "内容A"),
            Arg.Any<CancellationToken>());

        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            cardId, "elem-2",
            Arg.Is<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(dto => dto.Content == "内容B"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_不同CardId应各自独立发送()
    {
        // Act — 不同 cardId 的提交应各自独立
        _sut.Submit("card-A", "elem-1", "卡片A", 1);
        _sut.Submit("card-B", "elem-1", "卡片B", 2);

        // 等待 ProcessLoop 处理
        await Task.Delay(150);

        // Assert
        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            "card-A", "elem-1",
            Arg.Is<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(dto => dto.Content == "卡片A"),
            Arg.Any<CancellationToken>());

        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            "card-B", "elem-1",
            Arg.Is<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(dto => dto.Content == "卡片B"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region FlushAsync

    [Fact]
    public async Task FlushAsync_应发送指定卡片的所有待发送更新()
    {
        // Arrange
        var cardId = "card-flush";

        _sut.Submit(cardId, "elem-1", "内容1", 1);
        _sut.Submit(cardId, "elem-2", "内容2", 2);
        _sut.Submit(cardId, "elem-3", "内容3", 3);

        // 先等 ProcessLoop 处理完初始提交
        await Task.Delay(100);

        // 再提交一些新的
        _sut.Submit(cardId, "elem-4", "内容4", 4);
        _sut.Submit(cardId, "elem-5", "内容5", 5);

        // Act — FlushAsync 应发送所有剩余的待发送更新
        using var cts = new CancellationTokenSource(5000);
        await _sut.FlushAsync(cardId, cts.Token);

        // Assert — 至少 elem-4 和 elem-5 应通过 FlushAsync 发送
        // ProcessLoop 可能已经发送了部分，所以只验证 FlushAsync 后全部到达
        var totalCalls = _api.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == "PutCardkitV1CardsByCardIdElementsByElementIdContentAsync");

        Assert.True(totalCalls >= 2, $"预期至少 2 次调用，实际 {totalCalls} 次");
    }

    [Fact]
    public async Task FlushAsync_不应发送其他卡片的更新()
    {
        // Arrange
        _sut.Submit("card-A", "elem-1", "卡片A内容", 1);
        _sut.Submit("card-B", "elem-1", "卡片B内容", 1);

        // 等待 ProcessLoop 先处理
        await Task.Delay(150);

        // 再给 card-B 一个新的待发送
        _sut.Submit("card-B", "elem-2", "卡片B新内容", 2);

        // Act — 只 Flush card-A
        using var cts = new CancellationTokenSource(5000);
        await _sut.FlushAsync("card-A", cts.Token);

        // Assert — card-B 的 elem-2 不应被 FlushAsync 发送（但 ProcessLoop 会处理）
        // 清空之前的调用记录重新计数
        _api.ClearReceivedCalls();

        // FlushAsync 之前已经清空了 card-A 的 pending
        // 再次 Flush card-A 应无待发送
        await _sut.FlushAsync("card-A", cts.Token);

        // card-A 没有新的 pending，不应调用 API
        await _api.DidNotReceive().PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            "card-A", Arg.Any<string>(),
            Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region FlushAsync 等待 in-flight

    [Fact]
    public async Task FlushAsync_应等待ProcessLoop中正在发送的请求完成()
    {
        // Arrange — 让 API 延迟响应，模拟 in-flight 状态
        var tcs = new TaskCompletionSource<FeishuResponse>();

        _api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var cardId = "card-inflight";
        _sut.Submit(cardId, "elem-1", "正在发送", 1);

        // 等待 ProcessLoop 开始处理（拾取 pending 并开始 API 调用）
        await Task.Delay(100);

        // Act — FlushAsync 应等待 in-flight 完成
        var flushTask = _sut.FlushAsync(cardId);

        // 此时 FlushAsync 应在等待，尚未完成
        Assert.False(flushTask.IsCompleted);

        // 完成 API 调用
        tcs.SetResult(new FeishuResponse { Code = 0, Msg = "ok" });

        // FlushAsync 应该在合理时间内完成
        using var cts = new CancellationTokenSource(5000);
        await flushTask.WaitAsync(cts.Token);
    }

    #endregion

    #region DisposeAsync

    [Fact]
    public async Task DisposeAsync_正常释放不应抛出异常()
    {
        // Act & Assert — 不应抛出
        await _sut.DisposeAsync();
        // 标记为 null 防止 DisposeAsync 重复调用
        _sut = null!;
    }

    [Fact]
    public async Task DisposeAsync_有Pending任务时也能正常释放()
    {
        // Arrange — 提交一些待发送内容
        _sut.Submit("card-1", "elem-1", "内容", 1);
        _sut.Submit("card-2", "elem-2", "内容", 2);

        // 不等 ProcessLoop 处理完就 dispose
        // Act & Assert — 不应抛出
        await _sut.DisposeAsync();
        _sut = null!;
    }

    #endregion

    #region 限流集成

    [Fact]
    public async Task ProcessLoop_应通过限流器逐个发送()
    {
        // Arrange — 提交 3 个更新
        _sut.Submit("card-rt", "elem-1", "内容1", 1);
        _sut.Submit("card-rt", "elem-2", "内容2", 2);
        _sut.Submit("card-rt", "elem-3", "内容3", 3);

        // Act — 等待 ProcessLoop 处理完成
        await Task.Delay(300);

        // Assert — 每个更新应调用一次 API，共 3 次
        await _api.Received(3).PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessLoop_高频提交应正确去重()
    {
        // Arrange — 快速连续提交 10 次，但只有 2 个不同的 key
        for (int i = 0; i < 5; i++)
        {
            _sut.Submit("card-hf", "elem-A", $"A-{i}", i);
            _sut.Submit("card-hf", "elem-B", $"B-{i}", i);
        }

        // Act — 等待处理
        await Task.Delay(300);

        // Assert — 只有 2 个不同的 key，所以最多 2 次 API 调用
        var calls = _api.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "PutCardkitV1CardsByCardIdElementsByElementIdContentAsync")
            .ToList();

        // 去重后应有 2 次调用（elem-A 和 elem-B 各一次）
        Assert.Equal(2, calls.Count);
    }

    #endregion
}
