using FeishuNetSdk;
using FeishuNetSdk.Cardkit;
using ManInBlack.AI.Abstraction.Attributes;
using Microsoft.Extensions.Logging;

namespace FeishuAdaptor.FeishuCard;

/// <summary>
/// 全局卡片元素更新调度器，对所有飞书卡片元素更新进行去重和限流。
/// 限流规则：50 次/秒、1000 次/分钟。
/// </summary>
[ServiceRegister.Singleton]
public class CardUpdateScheduler : IAsyncDisposable
{
    private readonly IFeishuTenantApi _api;
    private readonly ILogger<CardUpdateScheduler> _logger;
    private const int MaxPerSecond = 50;
    private const int MaxPerMinute = 1000;

    // (cardId, elementId) -> 最新待发送内容
    private readonly Dictionary<(string CardId, string ElementId), PendingUpdate> _pending = new();
    private readonly object _pendingLock = new();

    // 每张卡片正在由 ProcessLoopAsync 发送的请求数量，FlushAsync 需要等待其归零
    private readonly Dictionary<string, int> _inFlightCountByCard = new();
    private readonly object _inFlightLock = new();
    private event Action<string>? InFlightCompleted;

    // 滑动窗口限流时间戳
    private readonly Queue<DateTime> _secondWindow = new();
    private readonly Queue<DateTime> _minuteWindow = new();
    private readonly object _rateLimitLock = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;

    public CardUpdateScheduler(IFeishuTenantApi api, ILogger<CardUpdateScheduler> logger)
    {
        _api = api;
        _logger = logger;
        _processingTask = ProcessLoopAsync();
    }

    /// <summary>
    /// 提交一个元素更新请求。如果该 (cardId, elementId) 已有待发送内容，则覆盖为最新值。
    /// </summary>
    public void Submit(string cardId, string elementId, string content, int sequence)
    {
        var key = (cardId, elementId);
        lock (_pendingLock)
        {
            if (_pending.TryGetValue(key, out var existing))
            {
                existing.Content = content;
                existing.Sequence = sequence;
            }
            else
            {
                _pending[key] = new PendingUpdate(content, sequence);
            }
        }
    }

    /// <summary>
    /// 立即发送指定卡片的所有待发送更新，确保内容在关闭流式模式前已送达。
    /// 同时等待 ProcessLoopAsync 中该卡片正在发送的请求完成。
    /// </summary>
    public async Task FlushAsync(string cardId, CancellationToken ct = default)
    {
        // 等待 ProcessLoopAsync 中该卡片正在发送的请求完成
        await WaitForInFlightAsync(cardId, ct);

        List<((string CardId, string ElementId) Key, PendingUpdate Update)> toFlush;
        lock (_pendingLock)
        {
            toFlush = _pending
                .Where(kvp => kvp.Key.CardId == cardId)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();

            foreach (var item in toFlush)
                _pending.Remove(item.Key);
        }

        foreach (var item in toFlush)
        {
            await WaitForRateLimitAsync(ct);

            await _api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
                item.Key.CardId,
                item.Key.ElementId,
                new PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto
                {
                    Content = item.Update.Content,
                    Sequence = item.Update.Sequence,
                },
                ct
            );
        }
    }

    /// <summary>
    /// 等待 ProcessLoopAsync 中该卡片正在发送的请求全部完成。
    /// </summary>
    private async Task WaitForInFlightAsync(string cardId, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            lock (_inFlightLock)
            {
                if (!_inFlightCountByCard.TryGetValue(cardId, out var count) || count == 0)
                    return;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

            void OnCompleted(string completedCardId)
            {
                if (completedCardId == cardId)
                    tcs.TrySetResult();
            }

            InFlightCompleted += OnCompleted;
            try
            {
                // 再次检查，避免在订阅事件前已经完成
                lock (_inFlightLock)
                {
                    if (!_inFlightCountByCard.TryGetValue(cardId, out var count) || count == 0)
                        return;
                }

                await tcs.Task;
            }
            finally
            {
                InFlightCompleted -= OnCompleted;
            }
        }
    }

    private async Task ProcessLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));

        while (await timer.WaitForNextTickAsync(_cts.Token))
        {
            // 快照并清空待发送队列
            List<((string CardId, string ElementId) Key, PendingUpdate Update)> batch;
            lock (_pendingLock)
            {
                if (_pending.Count == 0) continue;

                batch = new List<((string, string), PendingUpdate)>(_pending.Count);
                foreach (var kvp in _pending)
                    batch.Add((kvp.Key, kvp.Value));
                _pending.Clear();
            }

            // 记录每张卡片的 in-flight 数量
            var cardCounts = new Dictionary<string, int>();
            foreach (var item in batch)
            {
                if (!cardCounts.TryGetValue(item.Key.CardId, out var c))
                    c = 0;
                cardCounts[item.Key.CardId] = c + 1;
            }

            lock (_inFlightLock)
            {
                foreach (var (cardId, count) in cardCounts)
                {
                    _inFlightCountByCard.TryGetValue(cardId, out var existing);
                    _inFlightCountByCard[cardId] = existing + count;
                }
            }

            // 按限流规则逐个发送
            foreach (var item in batch)
            {
                try
                {
                    await WaitForRateLimitAsync(_cts.Token);

                    await _api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
                        item.Key.CardId,
                        item.Key.ElementId,
                        new PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto
                        {
                            Content = item.Update.Content,
                            Sequence = item.Update.Sequence,
                        },
                        _cts.Token
                    );
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "更新卡片元素失败 CardId={CardId} ElementId={ElementId} Sequence={Sequence}",
                        item.Key.CardId, item.Key.ElementId, item.Update.Sequence);
                }
                finally
                {
                    var cardId = item.Key.CardId;
                    lock (_inFlightLock)
                    {
                        if (_inFlightCountByCard.TryGetValue(cardId, out var c))
                        {
                            c--;
                            if (c <= 0)
                                _inFlightCountByCard.Remove(cardId);
                            else
                                _inFlightCountByCard[cardId] = c;
                        }
                    }

                    InFlightCompleted?.Invoke(cardId);
                }
            }
        }
    }

    /// <summary>
    /// 等待直到可以发送下一个请求（满足两个限流窗口）。
    /// </summary>
    private async Task WaitForRateLimitAsync(CancellationToken ct)
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
        lock (_rateLimitLock)
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
            if (_minuteWindow.Count >= MaxPerMinute)
            {
                return _minuteWindow.Peek() + TimeSpan.FromMinutes(1) - now;
            }

            // 检查秒限制
            if (_secondWindow.Count >= MaxPerSecond)
            {
                return _secondWindow.Peek() + TimeSpan.FromSeconds(1) - now;
            }

            return TimeSpan.Zero;
        }
    }

    private void RecordCall()
    {
        var now = DateTime.UtcNow;
        lock (_rateLimitLock)
        {
            _secondWindow.Enqueue(now);
            _minuteWindow.Enqueue(now);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            await _processingTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CardUpdateScheduler 关闭时发生异常");
        }

        _cts.Dispose();
    }

    private class PendingUpdate
    {
        public string Content;
        public int Sequence;

        public PendingUpdate(string content, int sequence)
        {
            Content = content;
            Sequence = sequence;
        }
    }
}
