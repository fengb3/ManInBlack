using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Storage;

/// <summary>
/// 工作空间策略模式
/// </summary>
public enum WorkspaceMode
{
    /// <summary>
    /// 每个用户独立目录：{RootPath}/workspaces/{userId}
    /// </summary>
    UserIsolated = 0,

    /// <summary>
    /// 使用进程当前工作目录
    /// </summary>
    CurrentDirectory = 1,

    /// <summary>
    /// 使用配置中指定的显式路径
    /// </summary>
    CustomPath = 2,
}

public class WorkspaceSettings
{
    public WorkspaceMode Mode { get; set; } = WorkspaceMode.UserIsolated;

    /// <summary>
    /// CustomPath 模式下的显式路径。仅当 Mode == CustomPath 时生效。
    /// </summary>
    public string? CustomPath { get; set; }
}

public class AgentStorageOptions
{
    public string RootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".man-in-black");

    public WorkspaceSettings Workspace { get; set; } = new();
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