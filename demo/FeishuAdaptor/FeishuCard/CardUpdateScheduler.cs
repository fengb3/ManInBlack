using System.Collections.Concurrent;
using FeishuAdaptor.FeishuCard.Cards;

namespace FeishuAdaptor.FeishuCard;

/// <summary>
/// 节流更新调度器 — 在固定时间窗口内聚合同一元素的多次修改，只发送一次最新值。
/// </summary>
public sealed class CardUpdateScheduler : IAsyncDisposable
{
    private readonly StreamingCard _card;
    private readonly TimeSpan _interval;
    private readonly ConcurrentDictionary<string, CardElement> _dirty = new();
    private readonly PeriodicTimer _timer;
    private readonly Task _loop;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    /// <param name="card">关联的 StreamingCard 实例。</param>
    /// <param name="interval">刷新间隔，默认 50ms。</param>
    public CardUpdateScheduler(StreamingCard card, TimeSpan? interval = null)
    {
        _card = card;
        _interval = interval ?? TimeSpan.FromMilliseconds(50);
        _timer = new PeriodicTimer(_interval);
        _loop = RunAsync(_cts.Token);
    }

    /// <summary>
    /// 标记元素为脏（需要更新）。同一 elementId 的多次调用只保留最新值。
    /// </summary>
    public void MarkDirty(string elementId, CardElement element)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        _dirty[elementId] = element;
    }

    /// <summary>
    /// 立即刷新所有待更新元素。用于需要确保一致性的场景。
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        var snapshot = DrainDirty();
        await ApplyUpdatesAsync(snapshot, ct);
    }

    /// <summary>
    /// 当前待更新元素数量。
    /// </summary>
    public int PendingCount => _dirty.Count;

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(ct))
            {
                var snapshot = DrainDirty();
                if (snapshot.Count > 0)
                {
                    try
                    {
                        await ApplyUpdatesAsync(snapshot, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // 调度器不应因单次失败而崩溃，后续周期会重试
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
    }

    private List<KeyValuePair<string, CardElement>> DrainDirty()
    {
        var list = new List<KeyValuePair<string, CardElement>>();
        foreach (var kvp in _dirty)
        {
            if (_dirty.TryRemove(kvp.Key, out var element))
                list.Add(new KeyValuePair<string, CardElement>(kvp.Key, element));
        }
        return list;
    }

    private async Task ApplyUpdatesAsync(
        List<KeyValuePair<string, CardElement>> updates,
        CancellationToken ct
    )
    {
        var tasks = updates.Select(kvp =>
            _card.PatchElementAsync(kvp.Key, kvp.Value, ct)
        );
        await Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();
        _timer.Dispose();
        try
        {
            await _loop;
        }
        catch
        {
            // 吞掉关闭异常
        }
        _cts.Dispose();
    }
}
