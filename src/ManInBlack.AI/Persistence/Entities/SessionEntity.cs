namespace ManInBlack.AI.Persistence.Entities;

/// <summary>会话实体（正规化后的一等公民）。</summary>
public sealed class SessionEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public long UserId { get; set; }
    /// <summary>关联的 <see cref="UserEntity.Id"/>（SelfHostUserId）。</summary>
    public UserEntity User { get; set; } = null!;
    public int Source { get; set; }   // SessionSource
    public DateTime CreatedAt { get; set; }
    public DateTime LastAt { get; set; }
}
