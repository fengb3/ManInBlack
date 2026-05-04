namespace FeishuAdaptor.FeishuCard;

/// <summary>
/// 滑动窗口限流器 — 支持秒级和分钟级双重限流。
/// 线程安全，可在多个生产者/消费者之间共享。
/// </summary>
public sealed class SlidingWindowRateLimiter
{
    private readonly int _maxPerSecond;
    private readonly int _maxPerMinute;
    private readonly Queue<DateTime> _secondWindow = new();
    private readonly Queue<DateTime> _minuteWindow = new();
    private readonly object _lock = new();

    public SlidingWindowRateLimiter(int maxPerSecond, int maxPerMinute)
    {
        _maxPerSecond = maxPerSecond;
        _maxPerMinute = maxPerMinute;
    }

    /// <summary>
    /// 等待直到可以发送下一个请求（满足秒级和分钟级两个限流窗口），然后记录本次调用。
    /// </summary>
    public async Task WaitForSlotAsync(CancellationToken ct)
    {
        while (true)
        {
            var delay = GetRequiredDelay();
            if (delay <= TimeSpan.Zero) break;

            await Task.Delay(delay, ct);
        }

        RecordCall();
    }

    private TimeSpan GetRequiredDelay()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            // 清理过期时间戳
            var minuteAgo = now - TimeSpan.FromMinutes(1);
            while (_minuteWindow.Count > 0 && _minuteWindow.Peek() < minuteAgo)
                _minuteWindow.Dequeue();

            var secondAgo = now - TimeSpan.FromSeconds(1);
            while (_secondWindow.Count > 0 && _secondWindow.Peek() < secondAgo)
                _secondWindow.Dequeue();

            // 检查分钟限制
            if (_minuteWindow.Count >= _maxPerMinute)
                return _minuteWindow.Peek() + TimeSpan.FromMinutes(1) - now;

            // 检查秒限制
            if (_secondWindow.Count >= _maxPerSecond)
                return _secondWindow.Peek() + TimeSpan.FromSeconds(1) - now;

            return TimeSpan.Zero;
        }
    }

    private void RecordCall()
    {
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            _secondWindow.Enqueue(now);
            _minuteWindow.Enqueue(now);
        }
    }
}
