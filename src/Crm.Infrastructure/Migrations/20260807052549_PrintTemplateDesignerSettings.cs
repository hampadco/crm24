using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PrintTemplateDesignerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowPdf",
                table: "PrintTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowWord",
                table: "PrintTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CustomCss",
                table: "PrintTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileNamePattern",
                table: "PrintTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FontFamily",
                table: "PrintTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FontSize",
                table: "PrintTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "PrintTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MarginBottom",
                table: "PrintTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MarginLeft",
                table: "PrintTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MarginRight",
                table: "PrintTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MarginTop",
                table: "PrintTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RepeatHeaderEachPage",
                table: "PrintTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ServiceProvider",
                table: "PrintTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ShowPageNumbers",
                table: "PrintTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TextDirection",
                table: "PrintTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WatermarkColor",
                table: "PrintTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WatermarkEnabled",
                table: "PrintTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WatermarkFontSize",
                table: "PrintTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WatermarkImagePath",
                table: "PrintTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WatermarkOpacity",
                table: "PrintTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WatermarkRotation",
                table: "PrintTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WatermarkText",
                table: "PrintTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WatermarkType",
                table: "PrintTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            // قالب‌های موجود باید فعال و قابل چاپ بمانند و تنظیمات پیش‌فرض بگیرند
            migrationBuilder.Sql("""
                UPDATE "PrintTemplates" SET
                    "IsActive" = TRUE,
                    "AllowPdf" = TRUE,
                    "AllowWord" = TRUE,
                    "ServiceProvider" = 'browser',
                    "TextDirection" = 'rtl',
                    "FontFamily" = 'shabnam',
                    "FontSize" = 12,
                    "MarginTop" = 12,
                    "MarginRight" = 12,
                    "MarginBottom" = 12,
                    "MarginLeft" = 12,
                    "WatermarkType" = 'text',
                    "WatermarkOpacity" = 12,
                    "WatermarkRotation" = -30,
                    "WatermarkFontSize" = 72,
                    "WatermarkColor" = '#9e9e9e';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowPdf",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "AllowWord",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "CustomCss",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "FileNamePattern",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "FontFamily",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "FontSize",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "MarginBottom",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "MarginLeft",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "MarginRight",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "MarginTop",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "RepeatHeaderEachPage",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "ServiceProvider",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "ShowPageNumbers",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "TextDirection",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "WatermarkColor",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "WatermarkEnabled",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "WatermarkFontSize",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "WatermarkImagePath",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "WatermarkOpacity",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "WatermarkRotation",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "WatermarkText",
                table: "PrintTemplates");

            migrationBuilder.DropColumn(
                name: "WatermarkType",
                table: "PrintTemplates");
        }
    }
}
