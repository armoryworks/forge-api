using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcontractAndReceivingInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inspected_at",
                table: "receiving_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "inspected_by_id",
                table: "receiving_records",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "inspected_quantity_accepted",
                table: "receiving_records",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "inspected_quantity_rejected",
                table: "receiving_records",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inspection_notes",
                table: "receiving_records",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "inspection_status",
                table: "receiving_records",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "qc_inspection_id",
                table: "receiving_records",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "inspection_frequency",
                table: "parts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "inspection_skip_after_n",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "receiving_inspection_template_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_receiving_inspection",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "subcontract_instructions",
                table: "operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "subcontract_lead_time_days",
                table: "operations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subcontract_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_return_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_by_id = table.Column<int>(type: "integer", nullable: true),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    shipping_tracking_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    return_tracking_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ncr_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subcontract_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_subcontract_orders__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subcontract_orders_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subcontract_orders_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subcontract_orders_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_job_id",
                table: "subcontract_orders",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_operation_id",
                table: "subcontract_orders",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_purchase_order_id",
                table: "subcontract_orders",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_status",
                table: "subcontract_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_vendor_id",
                table: "subcontract_orders",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subcontract_orders");

            migrationBuilder.DropColumn(
                name: "inspected_at",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "inspected_by_id",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "inspected_quantity_accepted",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "inspected_quantity_rejected",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "inspection_notes",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "inspection_status",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "qc_inspection_id",
                table: "receiving_records");

            migrationBuilder.DropColumn(
                name: "inspection_frequency",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "inspection_skip_after_n",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "receiving_inspection_template_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "requires_receiving_inspection",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "subcontract_instructions",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "subcontract_lead_time_days",
                table: "operations");
        }
    }
}
