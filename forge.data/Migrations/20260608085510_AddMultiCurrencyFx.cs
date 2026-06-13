using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiCurrencyFx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "settlement_fx_rate",
                table: "vendor_payment_applications",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "currency_id",
                table: "vendor_bills",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "fx_rate",
                table: "vendor_bills",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "settlement_fx_rate",
                table: "payment_applications",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "currency_id",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "fx_rate",
                table: "invoices",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bills_currency",
                table: "vendor_bills",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_currency_id",
                table: "invoices",
                column: "currency_id");

            migrationBuilder.AddForeignKey(
                name: "fk_invoices_currencies_currency_id",
                table: "invoices",
                column: "currency_id",
                principalTable: "currencies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_vendor_bills_currency",
                table: "vendor_bills",
                column: "currency_id",
                principalTable: "currencies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_invoices_currencies_currency_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_vendor_bills_currency",
                table: "vendor_bills");

            migrationBuilder.DropIndex(
                name: "ix_vendor_bills_currency",
                table: "vendor_bills");

            migrationBuilder.DropIndex(
                name: "ix_invoices_currency_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "settlement_fx_rate",
                table: "vendor_payment_applications");

            migrationBuilder.DropColumn(
                name: "currency_id",
                table: "vendor_bills");

            migrationBuilder.DropColumn(
                name: "fx_rate",
                table: "vendor_bills");

            migrationBuilder.DropColumn(
                name: "settlement_fx_rate",
                table: "payment_applications");

            migrationBuilder.DropColumn(
                name: "currency_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "fx_rate",
                table: "invoices");
        }
    }
}
