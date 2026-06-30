namespace ManInBlack.AI.Persistence.Entities;

/// <summary>
/// 状态快照实体。按 SessionId 整存整取整覆盖。
/// </summary>
public sealed class AgentStateSnapshotEntity
{
    public string SessionId { get; set; } = "";
    public string SavedAt { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}
