using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModuleStudioFieldBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BlockId",
                table: "Fields",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVisible",
                table: "Fields",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxLength",
                table: "Fields",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisibilityRuleJson",
                table: "Fields",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FieldBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsCollapsed = table.Column<bool>(type: "boolean", nullable: false),
                    VisibilityRuleJson = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_FieldBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldBlocks_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fields_BlockId",
                table: "Fields",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldBlocks_ModuleId",
                table: "FieldBlocks",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldBlocks_TenantId_ModuleId_Name",
                table: "FieldBlocks",
                columns: new[] { "TenantId", "ModuleId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Fields_FieldBlocks_BlockId",
                table: "Fields",
                column: "BlockId",
                principalTable: "FieldBlocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Fields_FieldBlocks_BlockId",
                table: "Fields");

            migrationBuilder.DropTable(
                name: "FieldBlocks");

            migrationBuilder.DropIndex(
                name: "IX_Fields_BlockId",
                table: "Fields");

            migrationBuilder.DropColumn(
                name: "BlockId",
                table: "Fields");

            migrationBuilder.DropColumn(
                name: "IsVisible",
                table: "Fields");

            migrationBuilder.DropColumn(
                name: "MaxLength",
                table: "Fields");

            migrationBuilder.DropColumn(
                name: "VisibilityRuleJson",
                table: "Fields");
        }
    }
}
