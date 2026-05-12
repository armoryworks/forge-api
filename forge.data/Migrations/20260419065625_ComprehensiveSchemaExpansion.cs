using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class ComprehensiveSchemaExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "auto_po_mode",
                table: "vendors",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_order_amount",
                table: "vendors",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bill_of_lading_number",
                table: "shipments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "estimated_delivery_date",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "freight_class",
                table: "shipments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "insured_value",
                table: "shipments",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_type",
                table: "shipments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "signature_required",
                table: "shipments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "shipment_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "handling_instructions",
                table: "shipment_lines",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "height",
                table: "shipment_lines",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_hazmat",
                table: "shipment_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "length",
                table: "shipment_lines",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "serial_numbers",
                table: "shipment_lines",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "weight",
                table: "shipment_lines",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width",
                table: "shipment_lines",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "exclude_from_auto_po",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "min_order_qty",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pack_size",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "safety_stock_qty",
                table: "parts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "vendor_id",
                table: "bomentries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reversed_movement_id",
                table: "bin_movements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auto_po_suggestions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    suggested_qty = table.Column<int>(type: "integer", nullable: false),
                    needed_by_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_sales_order_ids = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    converted_purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_po_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_auto_po_suggestions__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_auto_po_suggestions__purchase_orders_converted_purchase_order~",
                        column: x => x.converted_purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_auto_po_suggestions__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "domain_event_failures",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_payload = table.Column<string>(type: "text", nullable: false),
                    handler_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: false),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_domain_event_failures", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "follow_up_tasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    assigned_to_user_id = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_entity_id = table.Column<int>(type: "integer", nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_up_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_follow_up_tasks__asp_net_users_assigned_to_user_id",
                        column: x => x.assigned_to_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "training_scan_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    action_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    from_location_id = table.Column<int>(type: "integer", nullable: true),
                    to_location_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    shipment_id = table.Column<int>(type: "integer", nullable: true),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_scan_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_training_scan_logs__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_scan_devices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    device_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    paired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_scan_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_scan_devices__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bomentries_vendor_id",
                table: "bomentries",
                column: "vendor_id",
                filter: "vendor_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bin_movements_reversed_movement_id",
                table: "bin_movements",
                column: "reversed_movement_id",
                filter: "reversed_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_auto_po_suggestions_converted_purchase_order_id",
                table: "auto_po_suggestions",
                column: "converted_purchase_order_id",
                filter: "converted_purchase_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_auto_po_suggestions_part_id_status",
                table: "auto_po_suggestions",
                columns: new[] { "part_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_auto_po_suggestions_vendor_id",
                table: "auto_po_suggestions",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_domain_event_failures_status",
                table: "domain_event_failures",
                column: "status",
                filter: "status != 'Resolved'");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_assigned_to_user_id_status",
                table: "follow_up_tasks",
                columns: new[] { "assigned_to_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_source_entity_type_source_entity_id",
                table: "follow_up_tasks",
                columns: new[] { "source_entity_type", "source_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_training_scan_logs_job_id",
                table: "training_scan_logs",
                column: "job_id",
                filter: "job_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_training_scan_logs_part_id",
                table: "training_scan_logs",
                column: "part_id",
                filter: "part_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_training_scan_logs_user_id_scanned_at",
                table: "training_scan_logs",
                columns: new[] { "user_id", "scanned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_scan_devices_device_id",
                table: "user_scan_devices",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_scan_devices_user_id",
                table: "user_scan_devices",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_bin_movements_bin_movements_reversed_movement_id",
                table: "bin_movements",
                column: "reversed_movement_id",
                principalTable: "bin_movements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bomentries__vendors_vendor_id",
                table: "bomentries",
                column: "vendor_id",
                principalTable: "vendors",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bin_movements_bin_movements_reversed_movement_id",
                table: "bin_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_bomentries__vendors_vendor_id",
                table: "bomentries");

            migrationBuilder.DropTable(
                name: "auto_po_suggestions");

            migrationBuilder.DropTable(
                name: "domain_event_failures");

            migrationBuilder.DropTable(
                name: "follow_up_tasks");

            migrationBuilder.DropTable(
                name: "training_scan_logs");

            migrationBuilder.DropTable(
                name: "user_scan_devices");

            migrationBuilder.DropIndex(
                name: "ix_bomentries_vendor_id",
                table: "bomentries");

            migrationBuilder.DropIndex(
                name: "ix_bin_movements_reversed_movement_id",
                table: "bin_movements");

            migrationBuilder.DropColumn(
                name: "auto_po_mode",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "min_order_amount",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "bill_of_lading_number",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "estimated_delivery_date",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "freight_class",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "insured_value",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "service_type",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "signature_required",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "description",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "handling_instructions",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "height",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "is_hazmat",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "length",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "serial_numbers",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "weight",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "width",
                table: "shipment_lines");

            migrationBuilder.DropColumn(
                name: "exclude_from_auto_po",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "min_order_qty",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "pack_size",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "safety_stock_qty",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                table: "bomentries");

            migrationBuilder.DropColumn(
                name: "reversed_movement_id",
                table: "bin_movements");
        }
    }
}
