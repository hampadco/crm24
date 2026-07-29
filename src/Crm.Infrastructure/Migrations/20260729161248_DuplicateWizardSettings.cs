using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DuplicateWizardSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DuplicateCheckEnabled",
                table: "Modules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DuplicateIgnoreEmpty",
                table: "Modules",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateSyncPolicy",
                table: "Modules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "latest");

            migrationBuilder.AddColumn<bool>(
                name: "GlobalDuplicateEnabled",
                table: "Modules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsGlobalUniqueCheck",
                table: "Fields",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ماژول‌هایی که از قبل فیلد یکتا دارند را فعال نگه می‌داریم تا رفتار فعلی نشکند
            migrationBuilder.Sql("""
                UPDATE "Modules" m
                SET "DuplicateCheckEnabled" = TRUE
                WHERE EXISTS (
                    SELECT 1 FROM "Fields" f
                    WHERE f."ModuleId" = m."Id" AND f."IsUniqueCheck" = TRUE
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DuplicateCheckEnabled",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "DuplicateIgnoreEmpty",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "DuplicateSyncPolicy",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "GlobalDuplicateEnabled",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "IsGlobalUniqueCheck",
                table: "Fields");
        }
    }
}
