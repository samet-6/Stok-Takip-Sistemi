using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StokTakip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PasiflestirmeDamgasiVeHareketKilidi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "Categories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Append-only used to be enforced only by the absence of an endpoint, which binds
            // this application and nothing else. Here the rule sits next to the data, so a future
            // service, a stray script or a session in pgAdmin meets the same refusal. Correcting
            // a wrong movement stays what it always was: a compensating movement, not an edit.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION f_stock_movements_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION
                        'StockMovements is append-only: % is not allowed on this table', TG_OP;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_StockMovements_AppendOnly"
                BEFORE UPDATE OR DELETE ON "StockMovements"
                FOR EACH ROW EXECUTE FUNCTION f_stock_movements_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_StockMovements_AppendOnly" ON "StockMovements";""");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS f_stock_movements_append_only();");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Categories");
        }
    }
}
