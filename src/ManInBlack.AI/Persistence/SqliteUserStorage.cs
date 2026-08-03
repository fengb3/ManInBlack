using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// SQLite 实现的用户存储。SelfHostUserId = 自增 Id 的字符串形式。
/// 会话列表正规化到 Sessions 表（按 Source 区分），不再走 Users.SessionIdsJson blob。
/// </summary>
[ServiceRegister.Singleton.As<IUserStorage>]
public class SqliteUserStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteUserStorage> logger) : IUserStorage
{
    public async Task<UserEntry> GetOrCreateUser(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (entity is not null) return ToEntry(entity);

        entity = new UserEntity { UserId = userId };
        db.Users.Add(entity);
        await db.SaveChangesAsync();
        logger.LogInformation("创建用户 {UserId} (SelfHostUserId={SelfHostUserId})", userId, entity.Id);
        return ToEntry(entity);
    }

    public async Task SaveUserAsync(UserEntry userEntry)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.Users.FirstOrDefaultAsync(x => x.UserId == userEntry.UserId)
            ?? throw new InvalidOperationException($"用户不存在: {userEntry.UserId}");
        // MetadataJson/SessionIdsJson 列在 Finalize migration 前仍存在；
        // 正规化后 UserEntry 不再承载 Metadata/SessionIds，保留旧列值供数据搬迁读取。
        await db.SaveChangesAsync();
    }

    public async Task<string> CreateNewSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        var user = await GetOrCreateUser(userId);
        var sessionId = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        db.Sessions.Add(new SessionEntity
        {
            SessionId = sessionId,
            UserId = long.Parse(user.SelfHostUserId),
            Source = (int)source,
            CreatedAt = now,
            LastAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    public async Task<string?> GetLatestSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        await using var db = dbFactory.CreateDbContext();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (user is null) return null;
        var row = await db.Sessions.AsNoTracking()
            .Where(x => x.UserId == user.Id && x.Source == (int)source)
            .OrderByDescending(x => x.LastAt)
            .FirstOrDefaultAsync();
        return row?.SessionId;
    }

    private static UserEntry ToEntry(UserEntity e) => new()
    {
        UserId = e.UserId,
        SelfHostUserId = e.Id.ToString(),
        CreatedAt = e.CreatedAt,
    };
}
