using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ManInBlack.AI.Persistence;

/// <summary>
/// 每连接设置 busy_timeout,并发写抢锁时重试而非立刻抛 SQLITE_BUSY。
/// WAL 为库级持久设置,由启动期 MigrateManInBlackStorageAsync 设一次。
/// </summary>
internal sealed class SqliteInitInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=5000;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
