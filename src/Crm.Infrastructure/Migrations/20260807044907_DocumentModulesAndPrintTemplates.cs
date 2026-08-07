using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentModulesAndPrintTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConvertsToModule",
                table: "Modules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentKind",
                table: "Modules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsChildModule",
                table: "Modules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NextNumber",
                table: "Modules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NumberPrefix",
                table: "Modules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "FieldBlocks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LineLinkField",
                table: "FieldBlocks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineModuleName",
                table: "FieldBlocks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrintTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsHtmlEditor = table.Column<bool>(type: "boolean", nullable: false),
                    PageSize = table.Column<string>(type: "text", nullable: false),
                    Landscape = table.Column<bool>(type: "boolean", nullable: false),
                    HeaderHtml = table.Column<string>(type: "text", nullable: true),
                    BodyHtml = table.Column<string>(type: "text", nullable: true),
                    FooterHtml = table.Column<string>(type: "text", nullable: true),
                    ShareWithAllRoles = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_PrintTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintTemplates_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrintTemplateRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PrintTemplateId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_PrintTemplateRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintTemplateRoles_CrmRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "CrmRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrintTemplateRoles_PrintTemplates_PrintTemplateId",
                        column: x => x.PrintTemplateId,
                        principalTable: "PrintTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintTemplateRoles_PrintTemplateId",
                table: "PrintTemplateRoles",
                column: "PrintTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintTemplateRoles_RoleId",
                table: "PrintTemplateRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintTemplateRoles_TenantId_PrintTemplateId_RoleId",
                table: "PrintTemplateRoles",
                columns: new[] { "TenantId", "PrintTemplateId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintTemplates_ModuleId",
                table: "PrintTemplates",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintTemplates_TenantId_ModuleId_Name",
                table: "PrintTemplates",
                columns: new[] { "TenantId", "ModuleId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintTemplateRoles");

            migrationBuilder.DropTable(
                name: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "ConvertsToModule",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "DocumentKind",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "IsChildModule",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "NextNumber",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "NumberPrefix",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "FieldBlocks");

            migrationBuilder.DropColumn(
                name: "LineLinkField",
                table: "FieldBlocks");

            migrationBuilder.DropColumn(
                name: "LineModuleName",
                table: "FieldBlocks");
        }
    }
}
