using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services.Abstraction;

[ServiceRegister.Singleton.As<ISessionStorage>]
public class FileSessionStorage : ISessionStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AgentStorageOptions _options;
    private readonly ILogger<FileSessionStorage> _logger;

    public FileSessionStorage(IOptions<AgentStorageOptions> options, ILogger<FileSessionStorage> logger)
    {
        _options = options.Value;
        Directory.CreateDirectory(SessionDir);
        _logger = logger;
    }

    private string SessionDir => Path.Combine(_options.RootPath, "sessions");

    /// <inheritdoc/>
    public async Task SaveMessage(string sessionId, ChatMessage message)
    {
        var sessionFile = Path.Combine(SessionDir, $"{sessionId}.jsonl");
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await File.AppendAllTextAsync(sessionFile, json + Environment.NewLine);
    }

    /// <inheritdoc/>
    public async Task<IList<ChatMessage>> LoadMessages(string sessionId)
    {
        var messages = new List<ChatMessage>();
        var sessionFile = Path.Combine(SessionDir, $"{sessionId}.jsonl");

        _logger.LogInformation("Loading session {SessionId} from file {SessionFile}", sessionId, sessionFile);

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
