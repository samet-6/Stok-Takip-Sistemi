using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StokTakip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ListeIndexleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_CreatedAt",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_CreatedByUserId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CreatedAt_Id",
                table: "StockMovements",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CreatedByUserId_CreatedAt_Id",
                table: "StockMovements",
                columns: new[] { "CreatedByUserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId_CreatedAt_Id",
                table: "StockMovements",
                columns: new[] { "ProductId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt_Id",
                table: "Notifications",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Unread_ProductId",
                table: "Notifications",
                column: "ProductId",
                filter: "\"ReadAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_CreatedAt_Id",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_CreatedByUserId_CreatedAt_Id",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ProductId_CreatedAt_Id",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_CreatedAt_Id",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_Unread_ProductId",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CreatedAt",
                table: "StockMovements",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CreatedByUserId",
                table: "StockMovements",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt",
                descending: new bool[0]);
        }
    }
}
