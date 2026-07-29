using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModuleMenuAndCustomDataGin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MenuGroup",
                table: "Modules",
                type: "text",
                nullable: false,
                defaultValue: "tools");

            migrationBuilder.AddColumn<bool>(
                name: "ShowInMenu",
                table: "Modules",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""
                UPDATE "Modules" SET "ShowInMenu" = TRUE WHERE "IsActive" = TRUE;
                UPDATE "Modules" SET "MenuGroup" = 'marketing' WHERE "Name" IN ('leads', 'campaigns');
                UPDATE "Modules" SET "MenuGroup" = 'sales' WHERE "Name" IN ('contacts', 'organizations', 'opportunities', 'quotes', 'sales_orders', 'invoices', 'commissions', 'pricebooks', 'payments', 'product_sales');
                UPDATE "Modules" SET "MenuGroup" = 'support' WHERE "Name" IN ('tickets', 'contracts', 'warranties', 'services', 'calls');
                UPDATE "Modules" SET "MenuGroup" = 'inventory' WHERE "Name" IN ('products', 'vendors', 'purchase_orders', 'warehouses');
                UPDATE "Modules" SET "MenuGroup" = 'projects' WHERE "Name" IN ('projects', 'project_tasks', 'project_phases', 'leaves');
                UPDATE "Modules" SET "MenuGroup" = 'tools' WHERE "Name" IN ('tasks', 'events', 'documents');
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Records_CustomData_gin"
                ON "Records" USING GIN ("CustomData" jsonb_path_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Records_CustomData_gin";""");

            migrationBuilder.DropColumn(
                name: "MenuGroup",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "ShowInMenu",
                table: "Modules");
        }
    }
}
