using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI;
using ManInBlack.AI.Attributes;
using ManInBlack.AI.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentConsole.Middlewares;

/// <summary>
/// 会话持久化中间件，按用户 ID 恢复对话上下文
/// </summary>
[ServiceRegister.Scoped]
public class ReadPersistenceMiddleware(string directoryPath = "sessions") : AgentMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        Func<IAsyncEnumerable<ChatResponseUpdate>> next,
        CancellationToken cancellationToken = default)
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
        
        // Console.ForegroundColor = ConsoleColor.DarkRed;
        // Console.BackgroundColor = ConsoleColor.Magenta;
        // Console.WriteLine("add history messages Count" + messages.Count);
        // Console.ResetColor();

        // 执行管道
        await foreach (ChatResponseUpdate update in next().WithCancellation(cancellationToken))
        {
            yield return update;
        }
    }
}

/// <summary>
/// 保存会话持久化中间件，将对话消息追加保存到用户对应的会话文件中
/// </summary>
[ServiceRegister.Scoped]
public class SavePersistenceMiddleware : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context, 
        Func<IAsyncEnumerable<ChatResponseUpdate>> next, 
        CancellationToken cancellationToken = default)
    {
        var sessionStorage = context.ServiceProvider.GetRequiredService<SessionStorage>();

        // 保存用户消息
        sessionStorage.AppendChatMessage(new ChatMessage(ChatRole.User, context.UserInput));

        // 收集 assistant 文本，合并为一条消息
        var textBuffer = new StringBuilder(256);
        var reasoningBuffer = new StringBuilder(256);
        var functionCalls = new List<FunctionCallContent>();

        void FlushAssistant()
        {
            var contents = new List<AIContent>();
            if (reasoningBuffer.Length > 0)
                contents.Add(new TextReasoningContent(reasoningBuffer.ToString()));
            if (textBuffer.Length > 0)
                contents.Add(new TextContent(textBuffer.ToString()));
            if (contents.Count > 0)
            {
                sessionStorage.AppendChatMessage(new ChatMessage(ChatRole.Assistant, contents));
                textBuffer.Clear();
                reasoningBuffer.Clear();
            }
        }

        // 执行管道
        await foreach (ChatResponseUpdate update in next().WithCancellation(cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        textBuffer.Append(text.Text);
                        break;
                    case TextReasoningContent reasoning:
                        reasoningBuffer.Append(reasoning.Text);
                        break;
                    case FunctionCallContent fcc:
                        FlushAssistant();
                        functionCalls.Add(fcc);
                        break;
                    case FunctionResultContent frc:
                        // 遇到 tool result 时，先保存收集到的 function calls
                        if (functionCalls.Count > 0)
                        {
                            sessionStorage.AppendChatMessage(new ChatMessage(ChatRole.Assistant, functionCalls.Cast<AIContent>().ToList()));
                            functionCalls.Clear();
                        }
                        sessionStorage.AppendChatMessage(new ChatMessage(ChatRole.Tool, [frc]));
                        break;
                }
            }

            yield return update;
        }

        // 流结束后保存剩余内容
        FlushAssistant();
        if (functionCalls.Count > 0)
        {
            sessionStorage.AppendChatMessage(new ChatMessage(ChatRole.Assistant, functionCalls.Cast<AIContent>().ToList()));
        }
    }
}

[ServiceRegister.Scoped]
public class SessionStorage(Agent agent)
{
    private string SessionId { get; set; }

    private string UserId = agent.ParentId;

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

    private static JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
}