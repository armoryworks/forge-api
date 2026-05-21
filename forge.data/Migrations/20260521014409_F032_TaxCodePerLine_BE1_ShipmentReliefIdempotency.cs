using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class F032_TaxCodePerLine_BE1_ShipmentReliefIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inventory_relieved_at",
                table: "shipment_lines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                table: "sales_order_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                table: "invoice_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "integration_outbox_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                table: "integration_outbox_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "exemption_expiry_date",
                table: "customers",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "inventory_relieved_at",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "tax_code",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "tax_code",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "integration_outbox_entries");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                table: "integration_outbox_entries");

            migrationBuilder.DropColumn(
                name: "exemption_expiry_date",
                table: "customers");
        }
    }
}
