using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StokTakip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTurkishSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            // The folding function the generated columns below are computed with, and the one
            // SearchText.Fold maps to — so a search term and a stored key are produced by the
            // same code. Written by hand because EF has no model concept for a SQL function, and
            // it has to exist before the first column that references it.
            //
            // unaccent() is STABLE (it resolves its dictionary at run time) and a generated
            // column demands IMMUTABLE, so the dictionary is named explicitly and the result
            // wrapped. That is also what makes the value ctype-independent: unlike lower() alone,
            // this produces the same key on the dev database (ctype C) and in the container
            // (en_US.utf8), which is why the same search used to work in Docker and fail locally.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION f_fold(text) RETURNS text
                    LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                    AS $$ SELECT lower(unaccent('unaccent', $1)) $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Suppliers",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                collation: "tr-TR-x-icu",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Products",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                collation: "tr-TR-x-icu",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                collation: "tr-TR-x-icu",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "NameFolded",
                table: "Suppliers",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                computedColumnSql: "f_fold(\"Name\")",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "NameFolded",
                table: "Products",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                computedColumnSql: "f_fold(\"Name\")",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "SkuFolded",
                table: "Products",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                computedColumnSql: "f_fold(\"SKU\")",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "NameFolded",
                table: "Categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                computedColumnSql: "f_fold(\"Name\")",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_NameFolded",
                table: "Suppliers",
                column: "NameFolded")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_NameFolded",
                table: "Products",
                column: "NameFolded")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_SkuFolded",
                table: "Products",
                column: "SkuFolded")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_NameFolded",
                table: "Categories",
                column: "NameFolded")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_NameFolded",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Products_NameFolded",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SkuFolded",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Categories_NameFolded",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "NameFolded",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "NameFolded",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SkuFolded",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "NameFolded",
                table: "Categories");

            // After the generated columns are gone, before the extension it depends on.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS f_fold(text);");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Suppliers",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldCollation: "tr-TR-x-icu");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Products",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldCollation: "tr-TR-x-icu");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldCollation: "tr-TR-x-icu");
        }
    }
}
