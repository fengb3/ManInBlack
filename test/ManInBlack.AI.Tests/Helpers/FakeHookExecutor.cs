using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Hooks;

namespace ManInBlack.AI.Tests.Helpers;

/// <summary>
/// 内存版 IHookExecutor，记录所有调用并返回预设结果
/// </summary>
public class FakeHookExecutor : IHookExecutor
{
    public List<(HookPoint Point, HookContext Context)> ExecutedHooks { get; } = [];
    public HookResult Result { get; set; } = new();

    public Task<HookResult> ExecuteAsync(HookPoint point, HookContext context, CancellationToken ct = default)
    {
        ExecutedHooks.Add((point, context));
        return Task.FromResult(Result);
    }
}
