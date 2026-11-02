using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApiWebsiteConnect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_Key",
                table: "ApiKeys");

            migrationBuilder.AddColumn<int>(
                name: "ActAsUserId",
                table: "ApiKeys",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedOrigins",
                table: "ApiKeys",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "ApiKeys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyHash",
                table: "ApiKeys",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "KeyPrefix",
                table: "ApiKeys",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ApiKeys",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScopesJson",
                table: "ApiKeys",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ApiIdempotencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    ApiKeyId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiIdempotencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiWebhooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: false),
                    Events = table.Column<string>(type: "text", nullable: false),
                    ModuleNames = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FailCount = table.Column<int>(type: "integer", nullable: false),
                    LastStatus = table.Column<string>(type: "text", nullable: true),
                    LastAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiWebhooks", x => x.Id);
                });

            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pgcrypto;

                UPDATE "ApiKeys"
                SET "KeyHash" = encode(sha256(convert_to("Key", 'UTF8')), 'hex'),
                    "KeyPrefix" = left("Key", 12),
                    "Kind" = 'secret',
                    "ScopesJson" = '*'
                WHERE COALESCE("Key", '') <> '';

                UPDATE "ApiKeys"
                SET "KeyHash" = 'legacy-' || "Id"::text
                WHERE COALESCE("KeyHash", '') = '';

                UPDATE "ApiKeys"
                SET "Key" = ''
                WHERE COALESCE("Key", '') <> '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiIdempotencies_ApiKeyId_Key",
                table: "ApiIdempotencies",
                columns: new[] { "ApiKeyId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiIdempotencies_CreatedAtUtc",
                table: "ApiIdempotencies",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiIdempotencies");

            migrationBuilder.DropTable(
                name: "ApiWebhooks");

            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "ActAsUserId",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "AllowedOrigins",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "KeyHash",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "KeyPrefix",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "ScopesJson",
                table: "ApiKeys");

            migrationBuilder.Sql("""
                UPDATE "ApiKeys"
                SET "Key" = "KeyHash"
                WHERE COALESCE("Key", '') = '' AND COALESCE("KeyHash", '') <> '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_Key",
                table: "ApiKeys",
                column: "Key",
                unique: true);
        }
    }
}
