using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services;

[ServiceRegister.Singleton.As<ISessionStorage>]
public class FileSessionStorage(IOptions<AgentStorageOptions> options, ILogger<FileSessionStorage> logger)
    : ISessionStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AgentStorageOptions _options = options.Value;

    // TODO 内存里存一份
    // private Dictionary<string, List<ChatMessage>> _inMemorySessionMessages = new();

    private string SessionDir => Path.Combine(_options.RootPath, "sessions");

    /// <inheritdoc/>
    public async Task SaveMessage(string sessionId, ChatMessage message)
    {
        Directory.CreateDirectory(SessionDir);
        var sessionFile = Path.Combine(SessionDir, $"{sessionId}.jsonl");
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await File.AppendAllTextAsync(sessionFile, json + Environment.NewLine);
    }

    /// <inheritdoc/>
    public async Task<IList<ChatMessage>> LoadMessages(string sessionId)
    {
        Directory.CreateDirectory(SessionDir);
        var messages = new List<ChatMessage>();
        var sessionFile = Path.Combine(SessionDir, $"{sessionId}.jsonl");

        logger.LogInformation("Loading session {SessionId} from file {SessionFile}", sessionId, sessionFile);

        if (!File.Exists(sessionFile))
        {
            await File.Create(sessionFile).DisposeAsync(); // 创建空文件
            return messages;
        }

        await foreach (var line in File.ReadLinesAsync(sessionFile))
        {
            var message = JsonSerializer.Deserialize<ChatMessage>(line, JsonOptions);
            if (message != null)
                messages.Add(message);
        }

        return messages;
    }
}
