using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Core.Storage;

public static class GlobalConfiguration
{
    public static string AppFileRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $".man-in-black");
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

public record UserEntry
{
    public string UserId { get; set; }
    
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    public IList<string> SessionIds { get; set; } = new List<string>();
}

public static class UserEntryExtensions
{
    public static async Task<string> GetLatestSessionIdAsync(this UserEntry userEntry, IUserStorage userStorage)
    {
        var sessionId = userEntry.SessionIds.OrderBy(s => s).LastOrDefault();
        if (!string.IsNullOrEmpty(sessionId)) return sessionId;

        return await userEntry.CreateNewSessionIdAsync(userStorage);
    }

    public static async Task<string> CreateNewSessionIdAsync(this UserEntry userEntry, IUserStorage userStorage)
    {
        // create a new session id based on session create time
        var sessionId = $"{userEntry.UserId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        userEntry.SessionIds.Add(sessionId);
        await userStorage.SaveUserAsync(userEntry);
        return sessionId;
    }
}