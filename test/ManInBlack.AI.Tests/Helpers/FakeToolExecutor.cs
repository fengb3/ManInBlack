using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ManInBlack.AI.Abstraction.Tools;

namespace ManInBlack.AI.Tests.Helpers;

/// <summary>
/// 内存版 IToolExecutor，记录所有调用并返回预设结果
/// </summary>
public class FakeToolExecutor : IToolExecutor
{
    public int ExecuteCount { get; private set; }
    public List<ToolExecuteContext> ExecutedContexts { get; } = [];
    public object? Result { get; set; }
    public Exception? Error { get; set; }
    public Action<ToolExecuteContext>? OnExecute { get; set; }

    public Task ExecuteAsync(ToolExecuteContext ctx, CancellationToken ct = default)
    {
        ExecuteCount++;
        ExecutedContexts.Add(ctx);

        OnExecute?.Invoke(ctx);

        ctx.Result ??= Result;
        ctx.Error ??= Error;

        return Task.CompletedTask;
    }
}
