using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Storage;
using ManInBlack.AI.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services.Abstraction;

[ServiceRegister.Singleton.As<IUserStorage>]
public class FileUserStorage : IUserStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private string UsersDirRoot => Path.Combine(_options.RootPath, "users");

    public FileUserStorage(IOptions<AgentStorageOptions> options, ILogger<FileUserStorage> logger)
    {
        _options = options.Value;
        Directory.CreateDirectory(UsersDirRoot);
        _userIdMap = new JsonFileDictionary<string, string>(Path.Combine(UsersDirRoot, "userIdMap.json"));
        _logger = logger;

        _currId = _userIdMap.Values.Select(int.Parse).DefaultIfEmpty(0).Max();
    }

    private readonly AgentStorageOptions _options;
    private int _currId;

    private int GetNextId()
    {
        return Interlocked.Increment(ref _currId);
    }

    private readonly IDictionary<string, string> _userIdMap;
    private readonly ILogger<FileUserStorage> _logger;

    private string GetUserId(string oriId)
    {
        if (_userIdMap.TryGetValue(oriId, out var userId))
            return userId;

        var nextId = GetNextId();
        _userIdMap[oriId] = nextId.ToString();
        // await Task.CompletedTask; // 触发 JsonFileDictionary 的保存
        return nextId.ToString();
    }

    public async Task<UserEntry> GetOrCreateUser(string userId)
    {
        var selfHostUserId = GetUserId(userId);

        var userDir = Path.Combine(UsersDirRoot, selfHostUserId);
        Directory.CreateDirectory(userDir);
        var userEntryFile = Path.Combine(userDir, $"{selfHostUserId}.json");

        _logger.LogInformation("Getting user {UserId} from directory {UserDir}", userId, userDir);

        if (!File.Exists(userEntryFile))
        {
            await using var streamWriter = File.CreateText(userEntryFile);
            var userEntry = new UserEntry
            {
                UserId = userId,
                SelfHostUserId = selfHostUserId,
            };
            var json = JsonSerializer.Serialize(userEntry, JsonOptions);
            await streamWriter.WriteAsync(json);
            return userEntry;
        }
        else
        {
            // read from file
            var entry =  JsonSerializer.Deserialize<UserEntry>(await File.ReadAllTextAsync(userEntryFile), JsonOptions);

            if (entry == null)
            {
                throw new FileNotFoundException($"Could not find user {userId}");
            }

            return entry;
        }
    }

    public async Task SaveUserAsync(UserEntry userEntry)
    {
        var selfHostUserId = GetUserId(userEntry.UserId);
        var userDir = Path.Combine(UsersDirRoot, selfHostUserId);
        var userEntryFile = Path.Combine(userDir, $"{selfHostUserId}.json");
        var json = JsonSerializer.Serialize(userEntry, JsonOptions);
        await File.WriteAllTextAsync(userEntryFile, json);
    }

    public async Task<string> CreateNewSessionIdAsync(string userId)
    {
        var user = await GetOrCreateUser(userId);
        var sessionId = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        user.SessionIds.Add(sessionId);
        await SaveUserAsync(user);
        return sessionId;
    }
}
