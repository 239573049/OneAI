using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OneAI.AppMigrations
{
    /// <inheritdoc />
    public partial class SyncAppRequestLogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "AIRequestLogs");

            migrationBuilder.DropColumn(
                name: "MessageSummary",
                table: "AIRequestLogs");

            migrationBuilder.DropColumn(
                name: "RequestBody",
                table: "AIRequestLogs");

            migrationBuilder.DropColumn(
                name: "RequestParams",
                table: "AIRequestLogs");

            migrationBuilder.DropColumn(
                name: "ResponseSummary",
                table: "AIRequestLogs");

            migrationBuilder.AddColumn<int>(
                name: "CacheTokens",
                table: "AIRequestLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreateCacheTokens",
                table: "AIRequestLogs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheTokens",
                table: "AIRequestLogs");

            migrationBuilder.DropColumn(
                name: "CreateCacheTokens",
                table: "AIRequestLogs");

            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "AIRequestLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessageSummary",
                table: "AIRequestLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestBody",
                table: "AIRequestLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestParams",
                table: "AIRequestLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseSummary",
                table: "AIRequestLogs",
                type: "TEXT",
                nullable: true);
        }
    }
}
