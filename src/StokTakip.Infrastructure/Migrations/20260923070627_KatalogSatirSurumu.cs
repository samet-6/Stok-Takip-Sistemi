using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StokTakip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KatalogSatirSurumu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Suppliers",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Categories",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Categories");
        }
    }
}
