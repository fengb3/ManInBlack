using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Storage;

/// <summary>会话来源：区分用户交互会话与自动化触发会话。</summary>
public enum SessionSource
{
    /// <summary>用户交互（飞书 IM 等）。</summary>
    Interactive = 0,
    /// <summary>自动化 webhook 触发。</summary>
    Webhook = 1,
}

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