using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorBillPoLineMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "purchase_order_line_id",
                table: "vendor_bill_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "billed_quantity",
                table: "purchase_order_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bill_lines_po_line",
                table: "vendor_bill_lines",
                column: "purchase_order_line_id");

            migrationBuilder.AddForeignKey(
                name: "fk_vendor_bill_lines_po_line",
                table: "vendor_bill_lines",
                column: "purchase_order_line_id",
                principalTable: "purchase_order_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_vendor_bill_lines_po_line",
                table: "vendor_bill_lines");

            migrationBuilder.DropIndex(
                name: "ix_vendor_bill_lines_po_line",
                table: "vendor_bill_lines");

            migrationBuilder.DropColumn(
                name: "purchase_order_line_id",
                table: "vendor_bill_lines");

            migrationBuilder.DropColumn(
                name: "billed_quantity",
                table: "purchase_order_lines");
        }
    }
}
