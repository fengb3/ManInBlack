using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentConsole.Middlewares;

/// <summary>
/// 会话持久化中间件，按用户 ID 恢复对话上下文
/// </summary>
[ServiceRegister.Scoped]
#pragma warning disable CS9113 // Parameter is unread
public class ReadPersistenceMiddleware(string _directoryPath = "sessions") : AgentMiddleware
#pragma warning restore CS9113 // Parameter is unread
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sessionStorage = context.ServiceProvider.GetRequiredService<SessionStorage>();

        var messages = sessionStorage.Initialize(); // 从storage 里获取的消息, 还不包含 system prompt 和 user input

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
        var sessionStorage = context.ServiceProvider.GetRequiredService<SessionStorage>();

        // 用包装集合替换原始 Messages，每添加一条消息立即持久化
        var original = context.Messages;
        context.Messages = new PersistingMessageCollection(original, sessionStorage);

        await foreach (ChatResponseUpdate update in next().WithCancellation(ct))
        {
            yield return update;
        }

        context.Messages = original;
    }

    /// <summary>
    /// 在消息添加到集合时自动持久化（跳过 system 角色）
    /// </summary>
    private class PersistingMessageCollection(IList<ChatMessage> list, SessionStorage sessionStorage) : Collection<ChatMessage>(list)
    {
        protected override void InsertItem(int index, ChatMessage item)
        {
            base.InsertItem(index, item);
            if (item.Role != ChatRole.System)
                sessionStorage.AppendChatMessage(item);
        }
    }
}

[ServiceRegister.Scoped]
public class SessionStorage(AgentContext agentContext)
{
    private string SessionId { get; set; } = null!;

    private string UserId = agentContext.ParentId;

    public List<ChatMessage> Initialize()
    {

        // get latest session file
        var sessionDirectory = Path.Combine("sessions", UserId);

        Directory.CreateDirectory(sessionDirectory);

        var sessionFile = Directory.GetFiles(sessionDirectory, "*.session")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        if (sessionFile != null)
        {
            SessionId = Path.GetFileNameWithoutExtension(sessionFile);//sessionFile[..^".session".Length];// remove extension
        }
        else
        {
            SessionId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var newSessionFile = Path.Combine(sessionDirectory, $"{SessionId}.session");
            Directory.CreateDirectory(sessionDirectory);
            File.Create(newSessionFile).Dispose();
        }

        // 逐行读取
        var messages = new List<ChatMessage>();
        foreach (var line in File.ReadLines(Path.Combine(sessionDirectory, $"{SessionId}.session")))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var message = JsonSerializer.Deserialize<ChatMessage>(line, JsonOptions);
            if (message != null) messages.Add(message);
        }
        return messages;
    }

    public void AppendChatMessage(ChatMessage message)
    {
        if (SessionId == null)
            throw new InvalidOperationException("Session not initialized.");
        var sessionFile = Path.Combine("sessions", UserId, $"{SessionId}.session");
        var json        = JsonSerializer.Serialize(message, JsonOptions);
        File.AppendAllText(sessionFile, json + Environment.NewLine);
    }

    private static JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}