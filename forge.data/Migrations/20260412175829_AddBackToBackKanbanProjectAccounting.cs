using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBackToBackKanbanProjectAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kanban_cards",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    storage_location_id = table.Column<int>(type: "integer", nullable: true),
                    bin_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    number_of_bins = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    supply_source = table.Column<int>(type: "integer", nullable: false),
                    supply_vendor_id = table.Column<int>(type: "integer", nullable: true),
                    supply_work_center_id = table.Column<int>(type: "integer", nullable: true),
                    lead_time_days = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    last_triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_replenished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active_order_id = table.Column<int>(type: "integer", nullable: true),
                    active_order_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    trigger_count = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kanban_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_kanban_cards__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_kanban_cards__storage_locations_storage_location_id",
                        column: x => x.storage_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_kanban_cards__vendors_supply_vendor_id",
                        column: x => x.supply_vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_kanban_cards__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    sales_order_id = table.Column<int>(type: "integer", nullable: true),
                    budget_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    committed_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimate_at_completion_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    planned_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    revenue_recognized = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    percent_complete = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects__sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_projects_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "kanban_trigger_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kanban_card_id = table.Column<int>(type: "integer", nullable: false),
                    trigger_type = table.Column<int>(type: "integer", nullable: false),
                    triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fulfilled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fulfilled_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    order_id = table.Column<int>(type: "integer", nullable: true),
                    order_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    triggered_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kanban_trigger_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_kanban_trigger_logs_kanban_cards_kanban_card_id",
                        column: x => x.kanban_card_id,
                        principalTable: "kanban_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wbs_elements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<int>(type: "integer", nullable: false),
                    parent_element_id = table.Column<int>(type: "integer", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    budget_labor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    budget_material = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    budget_other = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    budget_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_labor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_material = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_other = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    percent_complete = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wbs_elements", x => x.id);
                    table.ForeignKey(
                        name: "fk_wbs_elements_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wbs_elements_wbs_elements_parent_element_id",
                        column: x => x.parent_element_id,
                        principalTable: "wbs_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wbs_cost_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wbs_element_id = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    source_entity_id = table.Column<int>(type: "integer", nullable: true),
                    entry_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wbs_cost_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_wbs_cost_entries__wbs_elements_wbs_element_id",
                        column: x => x.wbs_element_id,
                        principalTable: "wbs_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_card_number",
                table: "kanban_cards",
                column: "card_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_is_active",
                table: "kanban_cards",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_part_id",
                table: "kanban_cards",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_status",
                table: "kanban_cards",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_storage_location_id",
                table: "kanban_cards",
                column: "storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_supply_vendor_id",
                table: "kanban_cards",
                column: "supply_vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_work_center_id",
                table: "kanban_cards",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_trigger_logs_kanban_card_id",
                table: "kanban_trigger_logs",
                column: "kanban_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_trigger_logs_triggered_at",
                table: "kanban_trigger_logs",
                column: "triggered_at");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_trigger_logs_triggered_by_user_id",
                table: "kanban_trigger_logs",
                column: "triggered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_customer_id",
                table: "projects",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_project_number",
                table: "projects",
                column: "project_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_sales_order_id",
                table: "projects",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_status",
                table: "projects",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_cost_entries_category",
                table: "wbs_cost_entries",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_cost_entries_entry_date",
                table: "wbs_cost_entries",
                column: "entry_date");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_cost_entries_source_entity_type_source_entity_id",
                table: "wbs_cost_entries",
                columns: new[] { "source_entity_type", "source_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_wbs_cost_entries_wbs_element_id",
                table: "wbs_cost_entries",
                column: "wbs_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_elements_parent_element_id",
                table: "wbs_elements",
                column: "parent_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_elements_project_id",
                table: "wbs_elements",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_elements_project_id_code",
                table: "wbs_elements",
                columns: new[] { "project_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kanban_trigger_logs");

            migrationBuilder.DropTable(
                name: "wbs_cost_entries");

            migrationBuilder.DropTable(
                name: "kanban_cards");

            migrationBuilder.DropTable(
                name: "wbs_elements");

            migrationBuilder.DropTable(
                name: "projects");
        }
    }
}
