using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_TaxExempt_And_InvoiceCustomerPO : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customer_po",
                table: "invoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_tax_exempt",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tax_exemption_id",
                table: "customers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_is_tax_exempt",
                table: "customers",
                column: "is_tax_exempt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_is_tax_exempt",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "customer_po",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "is_tax_exempt",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "tax_exemption_id",
                table: "customers");
        }
    }
}
