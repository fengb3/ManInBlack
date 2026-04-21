using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Core.Attributes;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Services;

public interface ISessionStorage
{
    /// <summary>
    /// 保存一条消息
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="messages"></param>
    /// <returns></returns>
    Task SaveMessage(string sessionId, ChatMessage messages);
    
    /// <summary>
    /// 加载某个session下的所有消息
    /// </summary>
    /// <param name="sessionId"></param>
    /// <returns></returns>
    Task<ICollection<ChatMessage>> LoadMessages(string sessionId);
}

[ServiceRegister.Singleton.As<ISessionStorage>]
public class FileSessionStorage : ISessionStorage
{
    public string AppFileRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $".man-in-black");
    
    public string SessionDir => Path.Combine(AppFileRoot, "sessions");
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public FileSessionStorage()
    {
        Directory.CreateDirectory(AppFileRoot);
        Directory.CreateDirectory(SessionDir);
    }
    
    /// <inheritdoc/>
    public async Task SaveMessage(string sessionId, ChatMessage message)
    {
        var sessionFile = Path.Combine(SessionDir, $"{sessionId}.jsonl");
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await File.AppendAllTextAsync(sessionFile, json + Environment.NewLine);
    }

    /// <inheritdoc/>
    public async Task<ICollection<ChatMessage>> LoadMessages(string sessionId)
    {
        var messages = new List<ChatMessage>();
        var sessionFile = Path.Combine(SessionDir, $"{sessionId}.jsonl");
        if (!File.Exists(sessionFile))
            return messages;

        await foreach (var line in File.ReadLinesAsync(sessionFile))
        {
            var message = JsonSerializer.Deserialize<ChatMessage>(line, JsonOptions);
            if (message != null)
                messages.Add(message);
        }

        return messages;
    }
}