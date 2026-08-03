using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManInBlack.AI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeSessionsFinalize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 数据搬迁：旧 Users.SessionIdsJson blob → Sessions 行。必须在「加 FK / 删 blob 列」之前执行
            //（此时 Sessions 表已由 Prep 建好、blob 列仍在）。json_each 展开 JSON 数组；
            // CreatedAt 从 {userId}_{unix秒} 后缀解析；LastAt 取该会话最晚消息时间。
            // 仅取字符串元素（typeof='text'），跳过坏 blob 里的非字符串项。
            // INSERT OR IGNORE：旧 blob 可能有重复 sessionId（老 CreateNewSessionIdAsync 同秒创建未去重），
            // 重复行按唯一索引跳过（同 sessionId 数据一致，留一行即可）。
            migrationBuilder.Sql(@"
INSERT OR IGNORE INTO Sessions (SessionId, UserId, Source, CreatedAt, LastAt)
SELECT je.value,
       u.Id,
       0,
       COALESCE(datetime(substr(je.value, length(u.UserId) + 2), 'unixepoch'), datetime('now')),
       COALESCE((SELECT MAX(m.CreatedAt) FROM SessionMessages m WHERE m.SessionId = je.value), datetime('now'))
FROM Users u, json_each(u.SessionIdsJson) je
WHERE typeof(je.value) = 'text' AND je.value <> '';");

            // 孤儿清理：SessionMessages / AgentStateSnapshots 引用了不在任何 blob（故未进 Sessions）的
            // sessionId。加 FK 前必须删除，否则外键约束失败。
            migrationBuilder.Sql(@"DELETE FROM SessionMessages WHERE SessionId NOT IN (SELECT SessionId FROM Sessions);");
            migrationBuilder.Sql(@"DELETE FROM AgentStateSnapshots WHERE SessionId NOT IN (SELECT SessionId FROM Sessions);");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Sessions_SessionId",
                table: "Sessions",
                column: "SessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentStateSnapshots_Sessions_SessionId",
                table: "AgentStateSnapshots",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "SessionId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SessionMessages_Sessions_SessionId",
                table: "SessionMessages",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "SessionId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SessionIdsJson",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentStateSnapshots_Sessions_SessionId",
                table: "AgentStateSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_SessionMessages_Sessions_SessionId",
                table: "SessionMessages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Sessions_SessionId",
                table: "Sessions");

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SessionIdsJson",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // 反向重建 SessionIdsJson blob：从 Sessions 表按用户聚合（Sessions 表由 Prep 拥有，此处不删）。
            // json_quote 给每个 sessionId 加 JSON 引号，json_group_array 拼成数组（避免在 C# 源里写双引号字面量）。
            migrationBuilder.Sql(@"
UPDATE Users SET SessionIdsJson = COALESCE((
    SELECT json_group_array(json_quote(s.SessionId))
    FROM Sessions s WHERE s.UserId = Users.Id
), '[]');");
        }
    }
}
