using System.Collections.ObjectModel;
using ManInBlack.AI.Attributes;
using ManInBlack.AI.Middleware;
using Microsoft.Extensions.AI;

namespace AgentConsole.Middlewares;

/// <summary>
/// 消息丰富中间件，自动为所有添加到上下文的消息补全 CreatedAt 等元数据
/// </summary>
[ServiceRegister.Scoped]
public class MessageEnrichMiddleware : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        Func<IAsyncEnumerable<ChatResponseUpdate>> next,
        CancellationToken cancellationToken = default)
    {
        // 用包装集合替换原始 Messages，拦截所有添加操作
        var original = context.Messages;
        context.Messages = new EnrichingMessageCollection(original);

        await foreach (var update in next().WithCancellation(cancellationToken))
            yield return update;

        // 恢复原始列表（已包含所有带元数据的消息）
        context.Messages = original;
    }

    /// <summary>
    /// 在消息添加到集合时自动补全 CreatedAt（不覆盖已有值，如从持久化恢复的消息）
    /// </summary>
    private class EnrichingMessageCollection : Collection<ChatMessage>
    {
        public EnrichingMessageCollection(IList<ChatMessage> list) : base(list) { }

        protected override void InsertItem(int index, ChatMessage item)
        {
            item.CreatedAt ??= DateTimeOffset.UtcNow;
            base.InsertItem(index, item);
        }
    }
}
