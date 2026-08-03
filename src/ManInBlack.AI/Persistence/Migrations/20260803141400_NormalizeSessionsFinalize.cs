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
            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SessionIdsJson",
                table: "Users");

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
        }
    }
}
