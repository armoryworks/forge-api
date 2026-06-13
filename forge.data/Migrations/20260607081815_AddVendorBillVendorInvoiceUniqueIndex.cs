using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorBillVendorInvoiceUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_vendor_bills_vendor_invoice",
                table: "vendor_bills",
                columns: new[] { "vendor_id", "vendor_invoice_number" },
                unique: true,
                filter: "vendor_invoice_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_vendor_bills_vendor_invoice",
                table: "vendor_bills");
        }
    }
}
