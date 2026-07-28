using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModuleStudioPhaseD : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LinkFieldName",
                table: "Relations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateMatchMode",
                table: "Modules",
                type: "text",
                nullable: false,
                defaultValue: "or");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinkFieldName",
                table: "Relations");

            migrationBuilder.DropColumn(
                name: "DuplicateMatchMode",
                table: "Modules");
        }
    }
}
