using ManInBlack.AI.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// ManInBlack 持久化上下文。连接串由 DI 从 AgentStorageOptions.RootPath 注入。
/// </summary>
public class ManInBlackDbContext(DbContextOptions<ManInBlackDbContext> options) : DbContext(options)
{
    public DbSet<SessionMessageEntity> SessionMessages => Set<SessionMessageEntity>();
    public DbSet<AgentStateSnapshotEntity> AgentStateSnapshots => Set<AgentStateSnapshotEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionMessageEntity>(b =>
        {
            b.ToTable("SessionMessages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.SessionId).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.PayloadJson).IsRequired();
            b.HasIndex(x => new { x.SessionId, x.Id });
        });

        modelBuilder.Entity<AgentStateSnapshotEntity>(b =>
        {
            b.ToTable("AgentStateSnapshots");
            b.HasKey(x => x.SessionId);
            b.Property(x => x.SavedAt).IsRequired();
            b.Property(x => x.PayloadJson).IsRequired();
        });

        modelBuilder.Entity<UserEntity>(b =>
        {
            b.ToTable("Users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.UserId).IsRequired();
            b.HasIndex(x => x.UserId).IsUnique();
            b.Property(x => x.MetadataJson).IsRequired();
            b.Property(x => x.SessionIdsJson).IsRequired();
        });
    }
}
