using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StokTakip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SkuKarakterKurali : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_SKU",
                table: "Products",
                sql: "\"SKU\" ~ '^[A-Z0-9._/-]+$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_SKU",
                table: "Products");
        }
    }
}
