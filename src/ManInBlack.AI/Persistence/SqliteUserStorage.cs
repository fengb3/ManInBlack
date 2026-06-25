using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Persistence;

#pragma warning disable CS9113 // 参数是未读的
/// <summary>
/// SQLite 实现的用户存储。SelfHostUserId = 自增 Id 的字符串形式。
/// </summary>
public class SqliteUserStorage(
    IDbContextFactory<ManInBlackDbContext> dbFactory,
    ILogger<SqliteUserStorage> _logger) : IUserStorage
#pragma warning restore CS9113
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<UserEntry> GetOrCreateUser(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (entity is not null) return ToEntry(entity);

        entity = new UserEntity { UserId = userId };
        db.Users.Add(entity);
        await db.SaveChangesAsync();
        return ToEntry(entity);
    }

    public async Task SaveUserAsync(UserEntry userEntry)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.Users.FirstOrDefaultAsync(x => x.UserId == userEntry.UserId)
            ?? throw new InvalidOperationException($"用户不存在: {userEntry.UserId}");

        entity.MetadataJson = JsonSerializer.Serialize(userEntry.Metadata, JsonOptions);
        entity.SessionIdsJson = JsonSerializer.Serialize(userEntry.SessionIds, JsonOptions);
        await db.SaveChangesAsync();
    }

    public async Task<string> CreateNewSessionIdAsync(string userId)
    {
        var user = await GetOrCreateUser(userId);
        var sessionId = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        user.SessionIds.Add(sessionId);
        await SaveUserAsync(user);
        return sessionId;
    }

    private static UserEntry ToEntry(UserEntity e) => new()
    {
        UserId = e.UserId,
        SelfHostUserId = e.Id.ToString(),
        Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(e.MetadataJson, JsonOptions) ?? new(),
        SessionIds = JsonSerializer.Deserialize<List<string>>(e.SessionIdsJson, JsonOptions) ?? new(),
    };
}
