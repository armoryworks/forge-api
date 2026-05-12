using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveSourcingTermsToVendorPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lead_time_days",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "min_order_qty",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "pack_size",
                table: "parts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "lead_time_days",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "min_order_qty",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pack_size",
                table: "parts",
                type: "integer",
                nullable: true);
        }
    }
}
