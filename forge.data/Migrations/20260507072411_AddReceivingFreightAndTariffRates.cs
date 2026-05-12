using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingFreightAndTariffRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "incoterm",
                table: "vendor_parts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "freight_included",
                table: "vendor_part_price_tiers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_freight",
                table: "receiving_records",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "allocated_freight",
                table: "receiving_records",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "freight_allocation_method",
                table: "receiving_records",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "receipt_number",
                table: "receiving_records",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_freight",
                table: "purchase_orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "fx_rate",
                table: "purchase_orders",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fx_rate_source",
                table: "purchase_orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "incoterm",
                table: "purchase_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "quote_currency",
                table: "purchase_orders",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.CreateTable(
                name: "tariff_rates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    hts_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    country_of_origin = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    rate_pct = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariff_rates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receiving_records_receipt_number",
                table: "receiving_records",
                column: "receipt_number");

            migrationBuilder.CreateIndex(
                name: "ix_tariff_rates_hts_code_country_of_origin_effective_from",
                table: "tariff_rates",
                columns: new[] { "hts_code", "country_of_origin", "effective_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tariff_rates");

            migrationBuilder.DropIndex(
                name: "ix_receiving_records_receipt_number",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "incoterm",
                table: "vendor_parts");

            migrationBuilder.DropColumn(
                name: "freight_included",
                table: "vendor_part_price_tiers");

            migrationBuilder.DropColumn(
                name: "actual_freight",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "allocated_freight",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "freight_allocation_method",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "receipt_number",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "estimated_freight",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "fx_rate",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "fx_rate_source",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "incoterm",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "quote_currency",
                table: "purchase_orders");
        }
    }
}
