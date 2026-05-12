using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIoTEcommerceAndonBi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "andon_alerts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_id = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by_id = table.Column<int>(type: "integer", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    job_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_andon_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_andon_alerts__jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_andon_alerts__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bi_api_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    allowed_entity_sets_json = table.Column<string>(type: "jsonb", nullable: true),
                    allowed_ips_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bi_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ecommerce_integrations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    platform = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    encrypted_credentials = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    store_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    auto_import_orders = table.Column<bool>(type: "boolean", nullable: false),
                    sync_inventory = table.Column<bool>(type: "boolean", nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    part_mappings_json = table.Column<string>(type: "jsonb", nullable: true),
                    default_customer_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ecommerce_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_ecommerce_integrations_customers_default_customer_id",
                        column: x => x.default_customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "machine_connections",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    opc_ua_endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    security_policy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    auth_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    encrypted_credentials = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    poll_interval_ms = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_connections__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ecommerce_order_syncs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    integration_id = table.Column<int>(type: "integer", nullable: false),
                    external_order_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_order_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sales_order_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    order_data_json = table.Column<string>(type: "jsonb", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ecommerce_order_syncs", x => x.id);
                    table.ForeignKey(
                        name: "fk_ecommerce_order_syncs__sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ecommerce_order_syncs_ecommerce_integrations_integration_id",
                        column: x => x.integration_id,
                        principalTable: "ecommerce_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "machine_tags",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    connection_id = table.Column<int>(type: "integer", nullable: false),
                    tag_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    opc_node_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    warning_threshold_low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    warning_threshold_high = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    alarm_threshold_low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    alarm_threshold_high = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_tags_machine_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "machine_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "machine_data_points",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tag_id = table.Column<int>(type: "integer", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_data_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_data_points__machine_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "machine_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_acknowledged_by_id",
                table: "andon_alerts",
                column: "acknowledged_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_job_id",
                table: "andon_alerts",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_requested_at",
                table: "andon_alerts",
                column: "requested_at");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_requested_by_id",
                table: "andon_alerts",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_resolved_by_id",
                table: "andon_alerts",
                column: "resolved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_status",
                table: "andon_alerts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_work_center_id",
                table: "andon_alerts",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_bi_api_keys_is_active",
                table: "bi_api_keys",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_bi_api_keys_key_prefix",
                table: "bi_api_keys",
                column: "key_prefix");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_integrations_default_customer_id",
                table: "ecommerce_integrations",
                column: "default_customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_integrations_is_active",
                table: "ecommerce_integrations",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_integrations_platform",
                table: "ecommerce_integrations",
                column: "platform");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_order_syncs_integration_id",
                table: "ecommerce_order_syncs",
                column: "integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_order_syncs_integration_id_external_order_id",
                table: "ecommerce_order_syncs",
                columns: new[] { "integration_id", "external_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_order_syncs_sales_order_id",
                table: "ecommerce_order_syncs",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_order_syncs_status",
                table: "ecommerce_order_syncs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_machine_connections_is_active",
                table: "machine_connections",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_machine_connections_work_center_id",
                table: "machine_connections",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_data_points_tag_id",
                table: "machine_data_points",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_data_points_tag_id_timestamp",
                table: "machine_data_points",
                columns: new[] { "tag_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_machine_data_points_timestamp",
                table: "machine_data_points",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_machine_data_points_work_center_id",
                table: "machine_data_points",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_tags_connection_id",
                table: "machine_tags",
                column: "connection_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "andon_alerts");

            migrationBuilder.DropTable(
                name: "bi_api_keys");

            migrationBuilder.DropTable(
                name: "ecommerce_order_syncs");

            migrationBuilder.DropTable(
                name: "machine_data_points");

            migrationBuilder.DropTable(
                name: "ecommerce_integrations");

            migrationBuilder.DropTable(
                name: "machine_tags");

            migrationBuilder.DropTable(
                name: "machine_connections");
        }
    }
}
