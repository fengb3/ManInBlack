namespace ManInBlack.AI.Persistence.Entities;

/// <summary>
/// 用户实体。Id 自增对应 SelfHostUserId；UserId 为原始外部 id（唯一）。
/// </summary>
public sealed class UserEntity
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";       // Finalize 阶段删除
    public string SessionIdsJson { get; set; } = "[]";     // Finalize 阶段删除
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
