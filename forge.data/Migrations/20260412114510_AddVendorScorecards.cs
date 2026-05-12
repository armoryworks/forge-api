using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorScorecards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vendor_scorecards",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    total_purchase_orders = table.Column<int>(type: "integer", nullable: false),
                    total_lines_received = table.Column<int>(type: "integer", nullable: false),
                    on_time_deliveries = table.Column<int>(type: "integer", nullable: false),
                    late_deliveries = table.Column<int>(type: "integer", nullable: false),
                    early_deliveries = table.Column<int>(type: "integer", nullable: false),
                    avg_lead_time_days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    on_time_delivery_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_inspected = table.Column<int>(type: "integer", nullable: false),
                    total_accepted = table.Column<int>(type: "integer", nullable: false),
                    total_rejected = table.Column<int>(type: "integer", nullable: false),
                    total_ncrs = table.Column<int>(type: "integer", nullable: false),
                    quality_acceptance_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_spend = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    avg_price_variance_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_increase_count = table.Column<int>(type: "integer", nullable: false),
                    quantity_shortages = table.Column<int>(type: "integer", nullable: false),
                    quantity_overages = table.Column<int>(type: "integer", nullable: false),
                    quantity_accuracy_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    overall_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    grade = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calculation_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_scorecards", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_scorecards_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_scorecards_vendor_id_period_start",
                table: "vendor_scorecards",
                columns: new[] { "vendor_id", "period_start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vendor_scorecards");
        }
    }
}
