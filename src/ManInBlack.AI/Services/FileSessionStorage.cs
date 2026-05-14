using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services;

[ServiceRegister.Singleton.As<ISessionStorage>]
public class FileAgentStateStorage(IOptions<AgentStorageOptions> options, ILogger<FileAgentStateStorage> logger)
    : IAgentStateStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AgentStorageOptions _options = options.Value;

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
            await File.Create(sessionFile).DisposeAsync();
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

    /// <inheritdoc/>
    public async Task<AgentStateSnapshot?> LoadSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        var snapshotFile = Path.Combine(SessionDir, $"{sessionId}.state.json");
        if (!File.Exists(snapshotFile))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(snapshotFile, ct);
            return JsonSerializer.Deserialize<AgentStateSnapshot>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "快照文件损坏，将忽略: {File}", snapshotFile);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(SessionDir);
        var snapshotFile = Path.Combine(SessionDir, $"{sessionId}.state.json");
        var tempFile = snapshotFile + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(tempFile, json, ct);
            File.Move(tempFile, snapshotFile, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task DeleteSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        var snapshotFile = Path.Combine(SessionDir, $"{sessionId}.state.json");
        if (File.Exists(snapshotFile))
            File.Delete(snapshotFile);
        return Task.CompletedTask;
    }
}
