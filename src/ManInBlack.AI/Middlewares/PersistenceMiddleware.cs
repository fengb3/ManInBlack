using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 会话持久化中间件，按用户 ID 恢复对话上下文
/// </summary>
[ServiceRegister.Scoped]
public class ReadPersistenceMiddleware : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var workspace = context.ServiceProvider.GetRequiredService<IUserWorkspace>();
        
        // 重置对话 command
        if(UserInputCommandHelper.FetchCommand(context.UserInput, out var command, out var parameters))
        {
            // 如果是清除上下文的命令，直接清空持久化文件和上下文消息
            if (command is "clear" or "reset" or "new")
            {
                workspace.NewSession();
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
        

        var messages = workspace.Initialize(); // 从workspace 里获取的消息, 还不包含 system prompt 和 user input

        // 过滤掉 TextReasoningContent，不回传给模型（持久化保留全量，回传选择性过滤）
        foreach (var message in messages)
        {
            for (int i = message.Contents.Count - 1; i >= 0; i--)
            {
                if (message.Contents[i] is TextReasoningContent)
                    message.Contents.RemoveAt(i);
            }
        }

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
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var workspace = context.ServiceProvider.GetRequiredService<IUserWorkspace>();

        // 用包装集合替换原始 Messages，每添加一条消息立即持久化
        var original = context.Messages;
        context.Messages = new PersistingMessageCollection(original, workspace);

        await foreach (ChatResponseUpdate update in next().WithCancellation(ct))
        {
            yield return update;
        }

        context.Messages = original;
    }

    /// <summary>
    /// 在消息添加到集合时自动持久化（跳过 system 角色）
    /// </summary>
    private class PersistingMessageCollection(IList<ChatMessage> list, IUserWorkspace workspace) : Collection<ChatMessage>(list)
    {
        protected override void InsertItem(int index, ChatMessage item)
        {
            base.InsertItem(index, item);
            if (item.Role != ChatRole.System)
                workspace.AppendHistoryChatMessage(item);
        }
    }
}