using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Utils;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Services;

public static class GlobalConfiguration
{
    public static string AppFileRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $".man-in-black");
}

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
    Task<IList<ChatMessage>> LoadMessages(string sessionId);
}

[ServiceRegister.Singleton.As<ISessionStorage>]
public class FileSessionStorage : ISessionStorage
{
    
    public string SessionDir => Path.Combine(GlobalConfiguration.AppFileRoot, "sessions");
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public FileSessionStorage()
    {
        // Directory.CreateDirectory();
        Directory.CreateDirectory(SessionDir);
    }
    
    /// <inheritdoc/>
    public async Task SaveMessage(string sessionId, ChatMessage message)
    {
        var sessionFile = Path.Combine(SessionDir, $"{sessionId}.jsonl");
        // using var jsonFileList = new JsonFileList<ChatMessage>(sessionFile);
        // jsonFileList.Add(message);
        // await Task.CompletedTask;
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await File.AppendAllTextAsync(sessionFile, json + Environment.NewLine);
    }

    /// <inheritdoc/>
    public async Task<IList<ChatMessage>> LoadMessages(string sessionId)
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


public class FileUserStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
    public string UsersDirRoot => Path.Combine(GlobalConfiguration.AppFileRoot, "users");

    public FileUserStorage()
    {
        Directory.CreateDirectory(UsersDirRoot);
        _userIdMap = new JsonFileDictionary<string, string>(Path.Combine(UsersDirRoot, "userIdMap.json"));
    }
    
    private int _nextId;

    private int GetNextId()
    {
        return Interlocked.Increment(ref _nextId);
    }
    
    private IDictionary<string, string> _userIdMap ;

    private async Task<string> GetUserIdAsync(string oriId)
    {
        if(_userIdMap.TryGetValue(oriId, out var userId))
            return userId;
        
        var nextId = GetNextId();
        _userIdMap[oriId] = nextId.ToString();
        await Task.CompletedTask; // 触发 JsonFileDictionary 的保存
        return nextId.ToString();
    }
    
    public async Task<UserEntry?> GetOrCreateUser(string userId)
    {
        // Directory.CreateDirectory(UsersDirRoot);
        
        var selfHostUserId = await GetUserIdAsync(userId);
        
        var userDir = Path.Combine(UsersDirRoot, selfHostUserId);
        var userEntryFile = Path.Combine(userDir, $"{selfHostUserId}.json");
        if (!File.Exists(userDir))
        {
            var userEntry = new UserEntry
            {
                UserId = userId,
                Name = $"User-{selfHostUserId}",
                WorkingDirectory = Path.Combine(GlobalConfiguration.AppFileRoot, "users", selfHostUserId, "workspace")
            };
            var json = JsonSerializer.Serialize(userEntry, JsonOptions);
            await File.WriteAllTextAsync(userEntryFile, json);
            return userEntry;
        }
        else
        {
            // read from file
            return JsonSerializer.Deserialize<UserEntry>(await File.ReadAllTextAsync(userEntryFile), JsonOptions);
        }
    }

    public async Task SaveUser(UserEntry userEntry)
    {
        var selfHostUserId = await GetUserIdAsync(userEntry.UserId);
        var userDir = Path.Combine(UsersDirRoot, selfHostUserId);
        var userEntryFile = Path.Combine(userDir, $"{selfHostUserId}.json");
        var json = JsonSerializer.Serialize(userEntry, JsonOptions);
        await File.WriteAllTextAsync(userEntryFile, json);
    }
}

public record UserEntry
{
    public string UserId { get; set; }
    public string Name { get; set; }
    public string WorkingDirectory { get; set; }
}