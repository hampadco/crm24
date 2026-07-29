using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RelationKindAndRelatedFieldLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Relations",
                type: "integer",
                nullable: false,
                defaultValue: 1); // OneToMany

            migrationBuilder.AddColumn<string>(
                name: "RelatedFieldLabel",
                table: "Relations",
                type: "text",
                nullable: true);

            // ردیف‌های قدیمی چند‌به‌چند
            migrationBuilder.Sql("""UPDATE "Relations" SET "Kind" = 3 WHERE "IsManyToMany" = TRUE;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Relations");

            migrationBuilder.DropColumn(
                name: "RelatedFieldLabel",
                table: "Relations");
        }
    }
}
