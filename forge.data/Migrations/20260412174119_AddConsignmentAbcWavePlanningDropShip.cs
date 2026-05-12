using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConsignmentAbcWavePlanningDropShip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "abc_classification_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_parts = table.Column<int>(type: "integer", nullable: false),
                    class_acount = table.Column<int>(type: "integer", nullable: false),
                    class_bcount = table.Column<int>(type: "integer", nullable: false),
                    class_ccount = table.Column<int>(type: "integer", nullable: false),
                    class_athreshold_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    class_bthreshold_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    total_annual_usage_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lookback_months = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abc_classification_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consignment_agreements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    agreed_unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    min_stock_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    max_stock_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    invoice_on_consumption = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reconciliation_frequency_days = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consignment_agreements", x => x.id);
                    table.ForeignKey(
                        name: "fk_consignment_agreements__customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_consignment_agreements__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consignment_agreements__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pick_waves",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wave_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_to_id = table.Column<int>(type: "integer", nullable: true),
                    strategy = table.Column<int>(type: "integer", nullable: false),
                    total_lines = table.Column<int>(type: "integer", nullable: false),
                    picked_lines = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_waves", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "abc_classifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    classification = table.Column<int>(type: "integer", nullable: false),
                    annual_usage_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    annual_demand_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cumulative_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    run_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abc_classifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_abc_classifications__abc_classification_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "abc_classification_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_abc_classifications__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consignment_transactions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    agreement_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    extended_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    invoice_id = table.Column<int>(type: "integer", nullable: true),
                    bin_content_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consignment_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_consignment_transactions__invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_consignment_transactions__purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_consignment_transactions_consignment_agreements_agreement_id",
                        column: x => x.agreement_id,
                        principalTable: "consignment_agreements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pick_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wave_id = table.Column<int>(type: "integer", nullable: false),
                    shipment_line_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    from_location_id = table.Column<int>(type: "integer", nullable: false),
                    from_bin_id = table.Column<int>(type: "integer", nullable: true),
                    bin_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    picked_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    picked_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    picked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    short_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_pick_lines__pick_waves_wave_id",
                        column: x => x.wave_id,
                        principalTable: "pick_waves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pick_lines__shipment_lines_shipment_line_id",
                        column: x => x.shipment_line_id,
                        principalTable: "shipment_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_lines__storage_locations_from_location_id",
                        column: x => x.from_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_lines_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_abc_classifications_classification",
                table: "abc_classifications",
                column: "classification");

            migrationBuilder.CreateIndex(
                name: "ix_abc_classifications_part_id",
                table: "abc_classifications",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_abc_classifications_run_id",
                table: "abc_classifications",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_agreements_customer_id",
                table: "consignment_agreements",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_agreements_part_id",
                table: "consignment_agreements",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_agreements_status",
                table: "consignment_agreements",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_agreements_vendor_id",
                table: "consignment_agreements",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_transactions_agreement_id",
                table: "consignment_transactions",
                column: "agreement_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_transactions_invoice_id",
                table: "consignment_transactions",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_transactions_purchase_order_id",
                table: "consignment_transactions",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_from_location_id",
                table: "pick_lines",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_part_id",
                table: "pick_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_picked_by_user_id",
                table: "pick_lines",
                column: "picked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_shipment_line_id",
                table: "pick_lines",
                column: "shipment_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_wave_id",
                table: "pick_lines",
                column: "wave_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_assigned_to_id",
                table: "pick_waves",
                column: "assigned_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_status",
                table: "pick_waves",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_wave_number",
                table: "pick_waves",
                column: "wave_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abc_classifications");

            migrationBuilder.DropTable(
                name: "consignment_transactions");

            migrationBuilder.DropTable(
                name: "pick_lines");

            migrationBuilder.DropTable(
                name: "abc_classification_runs");

            migrationBuilder.DropTable(
                name: "consignment_agreements");

            migrationBuilder.DropTable(
                name: "pick_waves");
        }
    }
}
