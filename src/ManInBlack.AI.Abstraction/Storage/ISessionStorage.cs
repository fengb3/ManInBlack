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

/// <summary>
/// Agent 状态存储接口，合并消息持久化和状态快照能力
/// </summary>
public interface IAgentStateStorage : ISessionStorage
{
    /// <summary>
    /// 加载状态快照，无快照时返回 null
    /// </summary>
    Task<AgentStateSnapshot?> LoadSnapshotAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// 保存状态快照
    /// </summary>
    Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// 删除状态快照
    /// </summary>
    Task DeleteSnapshotAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>
/// 检查点保存策略，控制何时触发快照保存
/// </summary>
public interface ICheckpointPolicy
{
    /// <summary>
    /// 判断是否应该保存检查点
    /// </summary>
    /// <param name="phase">阶段标识："AfterToolCall" 或 "SessionEnd"</param>
    bool ShouldSave(string phase);
}