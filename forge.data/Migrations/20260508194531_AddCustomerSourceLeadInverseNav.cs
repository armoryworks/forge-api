using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSourceLeadInverseNav : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leads_converted_customer_id",
                table: "leads");

            migrationBuilder.CreateIndex(
                name: "ix_leads_converted_customer_id",
                table: "leads",
                column: "converted_customer_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leads_converted_customer_id",
                table: "leads");

            migrationBuilder.CreateIndex(
                name: "ix_leads_converted_customer_id",
                table: "leads",
                column: "converted_customer_id");
        }
    }
}
