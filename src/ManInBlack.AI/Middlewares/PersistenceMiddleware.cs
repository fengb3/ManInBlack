using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Services;
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

        // 重置对话 command
        if (
            UserInputCommandHelper.FetchCommand(
                (string)context.Items["UserInput"],
                out var command,
                out var parameters
            )
        )
        {
            // 如果是清除上下文的命令，直接清空持久化文件和上下文消息
            if (command is "clear" or "reset" or "new")
            {
                var userStorage = context.ServiceProvider.GetRequiredService<IUserStorage>();
                context.SessionId = await userStorage.CreateNewSessionIdAsync((string)context.Items["ParentId"]);
                context.Messages.Clear();
                yield return new ChatResponseUpdate
                {
                    AuthorName = null,
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent("已重置对话")],
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                yield break;
            }
        }

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

        // 将持久化消息添加到上下文中
        foreach (var message in messages)
        {
            context.Messages.Add(message);
        }

        // 执行管道
        await foreach (ChatResponseUpdate update in next().WithCancellation(ct))
        {
            yield return update;
        }
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
        var persisting = new PersistingMessageCollection(original, sessionStorage, context.SessionId);
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

        public PersistingMessageCollection(IList<ChatMessage> list, ISessionStorage storage, string sessionId)
            : base(list)
        {
            _consumerTask = Task.Run(async () =>
            {
                await foreach (var msg in _channel.Reader.ReadAllAsync())
                    await storage.SaveMessage(sessionId, msg);
            });
        }

        protected override void InsertItem(int index, ChatMessage item)
        {
            base.InsertItem(index, item);
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
            if (key is "SaveCheckpoint" or "ModelChoice" or "UserInput") continue;
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
