using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// 仅供 dotnet ef 设计时脚手架 migration 使用。运行期连接串由 DI 配置。
/// </summary>
internal sealed class ManInBlackDbContextDesignFactory : IDesignTimeDbContextFactory<ManInBlackDbContext>
{
    public ManInBlackDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ManInBlackDbContext>()
            .UseSqlite("Data Source=maninblack.db")
            .Options;
        return new ManInBlackDbContext(options);
    }
}
