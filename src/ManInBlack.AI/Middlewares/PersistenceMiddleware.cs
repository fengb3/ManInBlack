using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 会话持久化中间件，按用户 ID 恢复对话上下文
/// </summary>
[ServiceRegister.Scoped]
public class ReadPersistenceMiddleware : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        var sessionStorage = context.ServiceProvider.GetRequiredService<ISessionStorage>();

        // 恢复状态快照
        if (sessionStorage is IAgentStateStorage stateStorage)
        {
            var snapshot = await stateStorage.LoadSnapshotAsync(context.SessionId, ct);
            if (snapshot is not null)
            {
                foreach (var (key, value) in snapshot.Items)
                    context.Items[key] = value;
            }
        }

        // 注入 SaveCheckpoint 回调
        context.Items["SaveCheckpoint"] = (Func<string?, CancellationToken, Task>)(async (reason, token) =>
        {
            if (sessionStorage is not IAgentStateStorage stateStorage)
                return;
            var policy = context.ServiceProvider.GetService(typeof(ICheckpointPolicy)) as ICheckpointPolicy;
            if (policy is not null && !policy.ShouldSave(reason ?? "Unknown"))
                return;
            var snapshot = new AgentStateSnapshot
            {
                SessionId = context.SessionId,
                AgentName = context.AgentName,
                Items = PersistenceHelper.SerializeItems(context.Items),
                SavedAt = DateTimeOffset.UtcNow,
                CheckpointReason = reason,
            };
            try
            {
                await stateStorage.SaveSnapshotAsync(context.SessionId, snapshot, token);
            }
            catch (Exception ex)
            {
                var logger = context.ServiceProvider.GetService<ILogger<ReadPersistenceMiddleware>>();
                logger?.LogWarning(ex, "保存检查点失败: {SessionId}", context.SessionId);
            }
        });

        var messages = await sessionStorage.LoadMessages(context.SessionId); // 从workspace 里获取的消息, 还不包含 system prompt 和 user input

        // 过滤掉 TextReasoningContent，不回传给模型（持久化保留全量，回传选择性过滤）
        // 2026-04-27 - deepseek 要求把reasoning 回传回去 先注释掉这个过滤，保留所有内容回传模型，后续如果需要再调整过滤策略
        // foreach (var message in messages)
        // {
        //     for (int i = message.Contents.Count - 1; i >= 0; i--)
        //     {
        //         if (message.Contents[i] is TextReasoningContent)
        //             message.Contents.RemoveAt(i);r
        //     }
        // }

        // 修复孤儿 tool_calls：工具执行被打断后会残留「assistant(tool_calls) 无对应 tool 结果」，
        // 下一轮 API 会因此报 400。这里为缺失的结果补一条中断桩。已健全的历史不受影响。
        var sanitized = SanitizeToolCallHistory(messages);

        // 将持久化消息添加到上下文中
        foreach (var message in sanitized)
        {
            context.Messages.Add(message);
        }

        // 执行管道
        await foreach (ChatResponseUpdate update in next().WithCancellation(ct))
        {
            yield return update;
        }
    }

    /// <summary>
    /// 工具执行被打断时回填给 LLM 的结果文本。与 <see cref="AgentLoopMiddleware"/> 中的同名常量保持一致。
    /// </summary>
    private const string ToolInterruptedMessage = "工具执行已被中断，未获得结果。";

    /// <summary>
    /// 修复消息历史中两类会触发 400 的 tool_calls/tool 配对错乱：
    /// 1. assistant(tool_calls) 后缺少对应 tool 结果 → 补一条 <see cref="ToolInterruptedMessage"/> 桩消息
    ///    （典型来源：工具执行被打断，assistant(tool_calls) 落库却没有 tool 结果）。
    /// 2. tool 结果的前一条不是配对的 tool_calls（CallId 已被应答或不匹配）→ 丢弃该孤儿结果
    ///    （典型来源：打断后旧一轮的 tool 结果姗姗来迟，和新一轮消息交错落库）。
    /// 已健全的历史是 no-op。
    /// </summary>
    private static IList<ChatMessage> SanitizeToolCallHistory(IList<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return messages;

        var result = new List<ChatMessage>(messages.Count + 1);
        var pendingCallIds = new HashSet<string>();

        foreach (var msg in messages)
        {
            // assistant 的工具调用：先结算上一组未应答的调用，再登记本轮 CallId
            if (msg.Role == ChatRole.Assistant && msg.Contents.OfType<FunctionCallContent>().Any())
            {
                FlushPendingToolResults(result, pendingCallIds);
                result.Add(msg);
                foreach (var callId in msg.Contents.OfType<FunctionCallContent>().Select(c => c.CallId))
                    pendingCallIds.Add(callId);
                continue;
            }

            // tool 结果：仅保留对最近一次 tool_calls 的应答（CallId 仍在 pending 中）。
            // CallId 不在 pending 的结果，是与已应答/缺失 tool_calls 错配的残留——典型来源是
            // 「打断后旧一轮的 tool 结果姗姗来迟，和新一轮的消息交错落库」。这类孤儿 tool 结果
            // 会被 API 以 "tool must be a response to a preceding message with tool_calls" 拒绝，故丢弃。
            if (msg.Role == ChatRole.Tool)
            {
                var allResults = msg.Contents.OfType<FunctionResultContent>().ToList();
                var validResults = allResults.Where(fr => pendingCallIds.Remove(fr.CallId)).ToList();
                if (validResults.Count == allResults.Count)
                    result.Add(msg);                       // 全部有效：原样保留（保元数据）
                else if (validResults.Count > 0)
                    result.Add(new ChatMessage(ChatRole.Tool,
                        validResults.Select(r => (AIContent)r).ToList())); // 仅保留有效结果
                // 全部为孤儿/重复 → 丢弃整条 tool 消息
                continue;
            }

            // user / system / 无调用的 assistant：若有未应答调用，先补桩再追加
            FlushPendingToolResults(result, pendingCallIds);
            result.Add(msg);
        }

        // 尾部孤儿（最常见）：最后一条 assistant(tool_calls) 没有任何 tool 结果
        FlushPendingToolResults(result, pendingCallIds);

        return result;
    }

    private static void FlushPendingToolResults(List<ChatMessage> result, HashSet<string> pendingCallIds)
    {
        if (pendingCallIds.Count == 0)
            return;
        result.Add(new ChatMessage(ChatRole.Tool,
            pendingCallIds.Select(id => (AIContent)new FunctionResultContent(id, ToolInterruptedMessage)).ToList()));
        pendingCallIds.Clear();
    }
}

/// <summary>
/// 保存会话持久化中间件，每条新消息添加到 context 时立即持久化到会话文件
/// </summary>
[ServiceRegister.Scoped]
public class SavePersistenceMiddleware : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        var sessionStorage = context.ServiceProvider.GetRequiredService<ISessionStorage>();

        // 用包装集合替换原始 Messages，通过 Channel 异步持久化
        var original = context.Messages;
        var persisting = new PersistingMessageCollection(original, sessionStorage, context.SessionId, ct);
        context.Messages = persisting;

        await foreach (ChatResponseUpdate update in next().WithCancellation(ct))
        {
            yield return update;
        }

        context.Messages = original;
        await persisting.FlushAsync();

        // session 结束时保存最终检查点
        if (context.Items.TryGetValue("SaveCheckpoint", out var obj) && obj is Func<string?, CancellationToken, Task> save)
        {
            await save("SessionEnd", ct);
        }
    }

    /// <summary>
    /// 通过 Channel 异步持久化消息，避免 sync-over-async 死锁
    /// </summary>
    private class PersistingMessageCollection : Collection<ChatMessage>
    {
        private readonly Channel<ChatMessage> _channel = Channel.CreateUnbounded<ChatMessage>();
        private readonly Task _consumerTask;
        private readonly CancellationToken _ct;

        public PersistingMessageCollection(IList<ChatMessage> list, ISessionStorage storage, string sessionId, CancellationToken ct)
            : base(list)
        {
            _ct = ct;
            _consumerTask = Task.Run(async () =>
            {
                await foreach (var msg in _channel.Reader.ReadAllAsync())
                    await storage.SaveMessage(sessionId, msg);
            });
        }

        protected override void InsertItem(int index, ChatMessage item)
        {
            base.InsertItem(index, item);
            // 被取消（打断）后追加的消息不再落库：避免旧一轮残余输出与新轮次并发写同一会话，
            // 产生 tool 结果/消息交错的污染。取消前已追加的消息照常持久化。
            if (_ct.IsCancellationRequested)
                return;
            if (item.Role != ChatRole.System)
                _channel.Writer.TryWrite(item);
        }

        public async Task FlushAsync()
        {
            _channel.Writer.Complete();
            await _consumerTask;
        }
    }
}

file static class PersistenceHelper
{
    public static Dictionary<string, object> SerializeItems(IDictionary<string, object> items)
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, value) in items)
        {
            if (key == "SaveCheckpoint") continue;
            try
            {
                JsonSerializer.SerializeToElement(value);
                result[key] = value;
            }
            catch
            {
                // 不可序列化的值跳过
            }
        }
        return result;
    }
}
