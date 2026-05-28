using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSegment : Migration
    {
        // NOTE: `ef migrations add` also re-emitted four AddColumn calls
        // (shipment_lines.inventory_relieved_at, sales_order_lines.tax_code,
        // invoice_lines.tax_code, customers.exemption_expiry_date). Those columns
        // were ALREADY created by 20260521014409_F032_TaxCodePerLine_BE1_ShipmentReliefIdempotency;
        // they reappeared only because AppDbContextModelSnapshot was stale (model↔snapshot
        // drift). Re-adding them would fail on any DB that has F032 applied, so they are
        // intentionally removed here — this migration adds ONLY the new customer_segments
        // table. The regenerated snapshot now reflects all columns, fixing the drift.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_segments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    filter_criteria = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_segments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_segments_name",
                table: "customer_segments",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_segments");
        }
    }
}
