using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StokTakip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreatedAtKilidi : Migration
    {
        // StockMovements is not here: its append-only trigger already refuses every UPDATE.
        private static readonly string[] Tables =
            ["Categories", "Suppliers", "Products", "Notifications", "AspNetUsers"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D11: CreatedAt is written once, on insert — by AppDbContext's audit or a now()
            // default — and never again. The rule sits in the database for the same reason
            // append-only does: a pgAdmin session or a set-based update never meets EF.
            // IS DISTINCT FROM, not "was it in the SET list": Identity's UpdateAsync writes every
            // column back unchanged, and that must keep working.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION f_created_at_immutable() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."CreatedAt" IS DISTINCT FROM OLD."CreatedAt" THEN
                        RAISE EXCEPTION
                            '%."CreatedAt" is set on insert and cannot be changed', TG_TABLE_NAME;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """);

            foreach (var table in Tables)
                migrationBuilder.Sql($"""
                    CREATE TRIGGER "TR_{table}_CreatedAtLocked"
                    BEFORE UPDATE OF "CreatedAt" ON "{table}"
                    FOR EACH ROW EXECUTE FUNCTION f_created_at_immutable();
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
                migrationBuilder.Sql(
                    $"""DROP TRIGGER IF EXISTS "TR_{table}_CreatedAtLocked" ON "{table}";""");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS f_created_at_immutable();");
        }
    }
}
