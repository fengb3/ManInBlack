namespace ManInBlack.AI.Persistence.Entities;

/// <summary>
/// 会话消息持久化实体。PayloadJson 存整条 ChatMessage 序列化结果。
/// </summary>
public sealed class SessionMessageEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string PayloadJson { get; set; } = "";
}
