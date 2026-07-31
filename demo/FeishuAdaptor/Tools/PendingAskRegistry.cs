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
