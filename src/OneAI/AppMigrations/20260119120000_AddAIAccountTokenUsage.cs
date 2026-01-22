using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OneAI.AppMigrations
{
    public partial class AddAIAccountTokenUsage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PromptTokens",
                table: "AIAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CompletionTokens",
                table: "AIAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CacheTokens",
                table: "AIAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CreateCacheTokens",
                table: "AIAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromptTokens",
                table: "AIAccounts");

            migrationBuilder.DropColumn(
                name: "CompletionTokens",
                table: "AIAccounts");

            migrationBuilder.DropColumn(
                name: "CacheTokens",
                table: "AIAccounts");

            migrationBuilder.DropColumn(
                name: "CreateCacheTokens",
                table: "AIAccounts");
        }
    }
}
