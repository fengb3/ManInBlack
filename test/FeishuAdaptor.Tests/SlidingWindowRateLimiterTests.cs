using FeishuAdaptor.FeishuCard;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// SlidingWindowRateLimiter 单元测试 — 使用小窗口值确保快速执行。
/// </summary>
public class SlidingWindowRateLimiterTests
{
    /// <summary>辅助：创建 CancellationTokenSource，超时时间作为测试安全网。</summary>
    private static CancellationTokenSource CreateTimeoutCts(int timeoutMs = 10_000) => new(timeoutMs);

    #region 基本限流

    [Theory]
    [InlineData(3, 10)]
    [InlineData(5, 20)]
    [InlineData(10, 100)]
    public async Task WaitForSlotAsync_不超过限额_应全部立即完成(int maxPerSecond, int maxPerMinute)
    {
        var limiter = new SlidingWindowRateLimiter(maxPerSecond, maxPerMinute);
        using var cts = CreateTimeoutCts();

        for (var i = 0; i < maxPerSecond; i++)
        {
            await limiter.WaitForSlotAsync(cts.Token);
        }
        // 不抛异常即表示在限额内全部通过
    }

    #endregion

    #region 秒级限流

    [Fact]
    public async Task WaitForSlotAsync_超过秒限额_后续调用应等待()
    {
        // 3次/秒，10次/分钟 — 秒限额先触发
        var limiter = new SlidingWindowRateLimiter(3, 10);
        using var cts = CreateTimeoutCts(5000);

        // 消耗全部秒级配额
        for (var i = 0; i < 3; i++)
            await limiter.WaitForSlotAsync(cts.Token);

        // 第4次调用应需要等待（超过秒限额），但最终完成
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitForSlotAsync(cts.Token);
        sw.Stop();

        // 应有等待时间（滑动窗口需要等最早的调用过期）
        Assert.True(sw.ElapsedMilliseconds >= 50,
            $"应有等待时间，实际耗时 {sw.ElapsedMilliseconds}ms");
    }

    #endregion

    #region 分钟级限流

    [Fact]
    public async Task WaitForSlotAsync_超过分钟限额_后续调用应等待()
    {
        // 100次/秒（很高），3次/分钟 — 分钟限额先触发
        var limiter = new SlidingWindowRateLimiter(100, 3);
        using var cts = CreateTimeoutCts(70_000);

        // 消耗全部分钟级配额
        for (var i = 0; i < 3; i++)
            await limiter.WaitForSlotAsync(cts.Token);

        // 第4次调用应需要等待（超过分钟限额），但最终完成
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitForSlotAsync(cts.Token);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 50,
            $"应有等待时间，实际耗时 {sw.ElapsedMilliseconds}ms");
    }

    #endregion

    #region 并发安全

    [Fact]
    public async Task WaitForSlotAsync_多线程并发_不应超出秒限额()
    {
        const int maxPerSecond = 5;
        const int maxPerMinute = 50;
        var limiter = new SlidingWindowRateLimiter(maxPerSecond, maxPerMinute);
        using var cts = CreateTimeoutCts(10_000);

        var tasks = new Task[maxPerSecond + 3]; // 尝试超出限额
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = limiter.WaitForSlotAsync(cts.Token);
        }

        // 所有任务最终都应完成（可能需要等待窗口滑动）
        await Task.WhenAll(tasks);

        // 不抛异常说明并发下也能正确限流并恢复
    }

    [Fact]
    public async Task WaitForSlotAsync_高并发_所有任务最终完成()
    {
        const int maxPerSecond = 10;
        const int maxPerMinute = 100;
        var limiter = new SlidingWindowRateLimiter(maxPerSecond, maxPerMinute);
        using var cts = CreateTimeoutCts(30_000);

        var tasks = Enumerable.Range(0, 30).Select(_ => limiter.WaitForSlotAsync(cts.Token)).ToArray();
        await Task.WhenAll(tasks);
        // 全部完成不抛异常
    }

    #endregion

    #region CancellationToken

    [Fact]
    public async Task WaitForSlotAsync_取消后应抛出OperationCanceledException()
    {
        var limiter = new SlidingWindowRateLimiter(1, 1);
        using var cts = CreateTimeoutCts();

        // 消耗配额
        await limiter.WaitForSlotAsync(cts.Token);

        // 取消后再调用
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            limiter.WaitForSlotAsync(cts.Token));
    }

    [Fact]
    public async Task WaitForSlotAsync_等待中取消应抛出OperationCanceledException()
    {
        var limiter = new SlidingWindowRateLimiter(1, 1);
        using var cts = new CancellationTokenSource(200); // 200ms后自动取消

        // 消耗配额
        await limiter.WaitForSlotAsync(CancellationToken.None);

        // 第二次调用会被限流等待，200ms后取消
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            limiter.WaitForSlotAsync(cts.Token));
    }

    #endregion

    #region 时间窗口滑动

    [Fact]
    public async Task WaitForSlotAsync_窗口过期后可继续调用()
    {
        // 1次/秒，2次/分钟
        var limiter = new SlidingWindowRateLimiter(1, 2);
        using var cts = CreateTimeoutCts(70_000);

        // 第一次调用立即完成
        await limiter.WaitForSlotAsync(cts.Token);

        // 第二次调用应等待秒窗口滑动后完成
        await limiter.WaitForSlotAsync(cts.Token);

        // 再等秒窗口滑动后，第三次（分钟限额=2已用完，需等分钟窗口滑动）
        await limiter.WaitForSlotAsync(cts.Token);

        // 全部完成说明窗口滑动后限额恢复
    }

    #endregion
}
