using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StokTakip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KategoriAdAnahtariVeBildirimKisitlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Must exist before the generated column that calls it. Turkish case folding only
            // (GIDA = Gıda, Kıl ≠ Kil) — D9c. IMMUTABLE because a generated column demands it;
            // lower() and the ICU collation both live in pg_catalog, so it also resolves inside
            // index builds, where PostgreSQL narrows the search_path (f_fold cannot be indexed
            // directly for exactly that reason).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION f_name_key(text) RETURNS text
                    LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                    AS $$ SELECT lower($1 COLLATE "tr-TR-x-icu") $$;
                """);

            migrationBuilder.DropIndex(
                name: "UQ_Categories_Name",
                table: "Categories");

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "Categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                computedColumnSql: "f_name_key(\"Name\")",
                stored: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Quantity",
                table: "Notifications",
                sql: "\"Quantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_RequestedQuantity",
                table: "Notifications",
                sql: "(\"Type\" = 3) = (\"RequestedQuantity\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "UQ_Categories_NameKey",
                table: "Categories",
                column: "NameKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Quantity",
                table: "Notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_RequestedQuantity",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "UQ_Categories_NameKey",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "Categories");

            // After the generated column that depends on it is gone.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS f_name_key(text);");

            migrationBuilder.CreateIndex(
                name: "UQ_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);
        }
    }
}
