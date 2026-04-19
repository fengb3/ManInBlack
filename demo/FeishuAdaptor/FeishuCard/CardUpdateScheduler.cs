using FeishuNetSdk;
using FeishuNetSdk.Cardkit;
using ManInBlack.AI.Core.Attributes;
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
