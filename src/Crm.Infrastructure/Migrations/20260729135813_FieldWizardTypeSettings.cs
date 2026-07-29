using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FieldWizardTypeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DecimalDigits",
                table: "Fields",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FormulaExpression",
                table: "Fields",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IntegerDigits",
                table: "Fields",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationRulesJson",
                table: "Fields",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecimalDigits",
                table: "Fields");

            migrationBuilder.DropColumn(
                name: "FormulaExpression",
                table: "Fields");

            migrationBuilder.DropColumn(
                name: "IntegerDigits",
                table: "Fields");

            migrationBuilder.DropColumn(
                name: "ValidationRulesJson",
                table: "Fields");
        }
    }
}
