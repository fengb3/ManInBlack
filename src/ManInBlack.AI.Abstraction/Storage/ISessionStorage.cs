using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Storage;

public class AgentStorageOptions
{
    public string RootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".man-in-black");

    // public string WorkspaceRootPath { get; set; } = Path.Combine(
    //     Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "man_in_black_workspaces"
    // );
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
    
    public string SelfHostUserId { get; set; }
    
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    public IList<string> SessionIds { get; set; } = new List<string>();
}

public static class UserEntryExtensions
{
    public static string? GetLatestSessionId(this UserEntry userEntry)
        => userEntry.SessionIds.OrderBy(s => s).LastOrDefault();
}