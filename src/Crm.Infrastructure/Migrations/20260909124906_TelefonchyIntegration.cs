using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TelefonchyIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TelefonchyExtension",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Records_CustomData_cuid"
                ON "Records" (("CustomData" ->> 'cuid'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Records_CustomData_cuid";""");

            migrationBuilder.DropColumn(
                name: "TelefonchyExtension",
                table: "AspNetUsers");
        }
    }
}
