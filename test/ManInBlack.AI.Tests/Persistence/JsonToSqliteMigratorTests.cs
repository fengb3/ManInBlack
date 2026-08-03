using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Persistence;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Persistence;

public class JsonToSqliteMigratorTests
{
    /// <summary>
    /// 创建已迁移的测试工厂和迁移器实例。调用方负责 dispose ServiceProvider 并清理目录。
    /// </summary>
    private static async Task<(JsonToSqliteMigrator migrator, IDbContextFactory<ManInBlackDbContext> factory, ServiceProvider sp, string root)>
        CreateAsync()
    {
        var (factory, sp, root) = await SqliteTestHelpers.CreateFactoryAsync();
        var options = Options.Create(new AgentStorageOptions { RootPath = root });
        var migrator = new JsonToSqliteMigrator(factory, options, NullLogger<JsonToSqliteMigrator>.Instance);
        return (migrator, factory, sp, root);
    }

    /// <summary>
    /// 用真实 ChatMessage 序列化写 JSONL 文件，确保反序列化不丢失数据。
    /// </summary>
    private static void WriteRealJsonLl(string path, params ChatMessage[] messages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var opts = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using var w = File.CreateText(path);
        foreach (var m in messages) w.WriteLine(JsonSerializer.Serialize(m, opts));
    }

    [Fact]
    public async Task Migrate_ImportsMessagesSnapshotsUsers()
    {
        var (migrator, factory, sp, root) = await CreateAsync();
        try
        {
            // 造 sessions/s1.jsonl(2 条真实 ChatMessage)
            WriteRealJsonLl(Path.Combine(root, "sessions", "s1.jsonl"),
                new ChatMessage(ChatRole.User, "hi"),
                new ChatMessage(ChatRole.Assistant, "yo"));

            // 造 sessions/s1.state.json
            await File.WriteAllTextAsync(Path.Combine(root, "sessions", "s1.state.json"),
                JsonSerializer.Serialize(new AgentStateSnapshot { SessionId = "s1", SystemPrompt = "p", SavedAt = DateTimeOffset.UtcNow }));

            // 造 users/userIdMap.json + users/3.json
            Directory.CreateDirectory(Path.Combine(root, "users"));
            await File.WriteAllTextAsync(Path.Combine(root, "users", "userIdMap.json"),
                JsonSerializer.Serialize(new Dictionary<string, string> { ["ext-1"] = "3" }));
            await File.WriteAllTextAsync(Path.Combine(root, "users", "3.json"),
                JsonSerializer.Serialize(new { UserId = "ext-1", SelfHostUserId = "3", SessionIds = new List<string> { "ext-1_1" } }));

            var summary = await migrator.MigrateAsync();

            Assert.Equal(2, summary.Messages);
            Assert.Equal(1, summary.Snapshots);
            Assert.Equal(1, summary.Users);

            await using var db = factory.CreateDbContext();
            Assert.Equal(2, await db.SessionMessages.CountAsync());
            Assert.Single(await db.AgentStateSnapshots.ToListAsync());
            var user = await db.Users.SingleAsync();
            Assert.Equal("ext-1", user.UserId);
            Assert.Equal(3, user.Id); // 保留原数字内部 id
            Assert.Contains("ext-1_1", JsonSerializer.Deserialize<List<string>>(user.SessionIdsJson)!);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Migrate_IsIdempotent_SecondRunSkipsAll()
    {
        var (migrator, factory, sp, root) = await CreateAsync();
        try
        {
            WriteRealJsonLl(Path.Combine(root, "sessions", "s1.jsonl"),
                new ChatMessage(ChatRole.User, "hi"));

            await migrator.MigrateAsync();
            var second = await migrator.MigrateAsync();

            Assert.Equal(0, second.Messages);
            Assert.True(second.Skipped >= 1);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Migrate_NoOldData_IsNoOp()
    {
        var (migrator, factory, sp, root) = await CreateAsync();
        try
        {
            var summary = await migrator.MigrateAsync();
            Assert.Equal(0, summary.Messages + summary.Snapshots + summary.Users);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Migrate_PreservesExplicitId_NextAutoIncrementContinues()
    {
        var (migrator, factory, sp, root) = await CreateAsync();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "users"));
            await File.WriteAllTextAsync(Path.Combine(root, "users", "userIdMap.json"),
                JsonSerializer.Serialize(new Dictionary<string, string> { ["ext-old"] = "7" }));
            await File.WriteAllTextAsync(Path.Combine(root, "users", "7.json"),
                JsonSerializer.Serialize(new { UserId = "ext-old", SelfHostUserId = "7" }));

            await migrator.MigrateAsync();

            // 迁移后新建用户，自增 Id 应 > 7，不与已迁值冲突
            var userStorage = new SqliteUserStorage(factory, NullLogger<SqliteUserStorage>.Instance);
            var newUser = await userStorage.GetOrCreateUser("ext-new");
            Assert.True(int.Parse(newUser.SelfHostUserId) > 7);
        }
        finally
        {
            sp.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
