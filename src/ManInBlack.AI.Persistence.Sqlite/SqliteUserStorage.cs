using System.Security.Cryptography;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// SQLite 实现的用户存储。SelfHostUserId = 自增 Id 的字符串形式。
/// 会话列表正规化到 Sessions 表（按 Source 区分），不再走 Users.SessionIdsJson blob。
/// 会话 ID 格式为 <c>{userId}_{unix秒}_{random}</c>，并对唯一约束冲突做有限重试兜底。
/// </summary>
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
        // 正规化后 UserEntry 仅承载 UserId/Id；会话列表由 Sessions 表管理（CreateNewSessionIdAsync 写入）。
        await db.SaveChangesAsync();
    }

    public async Task<string> CreateNewSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive)
    {
        var user = await GetOrCreateUser(userId);
        var userIdLong = long.Parse(user.SelfHostUserId);
        await using var db = dbFactory.CreateDbContext();

        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var sessionId = GenerateSessionId(userId);
            var now = DateTime.UtcNow;
            db.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                UserId = userIdLong,
                Source = (int)source,
                CreatedAt = now,
                LastAt = now,
            });

            try
            {
                await db.SaveChangesAsync();
                return sessionId;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // 同秒 + 同随机发生碰撞，撤销变更并 retry
                logger.LogWarning(ex, "创建 SessionId 发生碰撞,user={UserId},attempt={Attempt}", userId, attempt + 1);
                db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException($"无法在 {maxRetries} 次尝试内创建唯一 SessionId");
    }

    private static string GenerateSessionId(string userId)
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(3))[..6];
        return $"{userId}_{unixSeconds}_{random}";
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqliteException sqlite && sqlite.SqliteErrorCode == 19)
            return true;
        return ex.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
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
