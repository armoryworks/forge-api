using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "mrp_planned_order_id",
                table: "purchase_order_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "demand_fence_days",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "fixed_order_quantity",
                table: "parts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_mrp_planned",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "lot_sizing_rule",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "minimum_order_quantity",
                table: "parts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "order_multiple",
                table: "parts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "planning_fence_days",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "mrp_planned_order_id",
                table: "jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mrp_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    run_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_simulation = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    planning_horizon_days = table.Column<int>(type: "integer", nullable: false),
                    total_demand_count = table.Column<int>(type: "integer", nullable: false),
                    total_supply_count = table.Column<int>(type: "integer", nullable: false),
                    planned_order_count = table.Column<int>(type: "integer", nullable: false),
                    exception_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    initiated_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mrp_exceptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mrp_run_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    exception_type = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    suggested_action = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_exceptions__mrp_runs_mrp_run_id",
                        column: x => x.mrp_run_id,
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mrp_exceptions__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_planned_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mrp_run_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    order_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_firmed = table.Column<bool>(type: "boolean", nullable: false),
                    released_purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    released_job_id = table.Column<int>(type: "integer", nullable: true),
                    parent_planned_order_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_planned_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders__mrp_runs_mrp_run_id",
                        column: x => x.mrp_run_id,
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders__purchase_orders_released_purchase_order_id",
                        column: x => x.released_purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders_jobs_released_job_id",
                        column: x => x.released_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders_mrp_planned_orders_parent_planned_order_~",
                        column: x => x.parent_planned_order_id,
                        principalTable: "mrp_planned_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "mrp_supplies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mrp_run_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_entity_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    available_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    allocated_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_supplies", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_supplies__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mrp_supplies_mrp_runs_mrp_run_id",
                        column: x => x.mrp_run_id,
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mrp_demands",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mrp_run_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_entity_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    required_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_dependent = table.Column<bool>(type: "boolean", nullable: false),
                    parent_planned_order_id = table.Column<int>(type: "integer", nullable: true),
                    bom_level = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_demands", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_demands__mrp_planned_orders_parent_planned_order_id",
                        column: x => x.parent_planned_order_id,
                        principalTable: "mrp_planned_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mrp_demands__mrp_runs_mrp_run_id",
                        column: x => x.mrp_run_id,
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mrp_demands__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_mrp_planned_order_id",
                table: "purchase_order_lines",
                column: "mrp_planned_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_mrp_planned_order_id",
                table: "jobs",
                column: "mrp_planned_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mrp_demands_mrp_run_id",
                table: "mrp_demands",
                column: "mrp_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_demands_parent_planned_order_id",
                table: "mrp_demands",
                column: "parent_planned_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_demands_part_id",
                table: "mrp_demands",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_exceptions_mrp_run_id",
                table: "mrp_exceptions",
                column: "mrp_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_exceptions_part_id",
                table: "mrp_exceptions",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_exceptions_resolved_by_user_id",
                table: "mrp_exceptions",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_mrp_run_id",
                table: "mrp_planned_orders",
                column: "mrp_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_parent_planned_order_id",
                table: "mrp_planned_orders",
                column: "parent_planned_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_part_id",
                table: "mrp_planned_orders",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_released_job_id",
                table: "mrp_planned_orders",
                column: "released_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_released_purchase_order_id",
                table: "mrp_planned_orders",
                column: "released_purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_status",
                table: "mrp_planned_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_runs_initiated_by_user_id",
                table: "mrp_runs",
                column: "initiated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_runs_run_number",
                table: "mrp_runs",
                column: "run_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mrp_runs_status",
                table: "mrp_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_supplies_mrp_run_id",
                table: "mrp_supplies",
                column: "mrp_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_supplies_part_id",
                table: "mrp_supplies",
                column: "part_id");

            migrationBuilder.AddForeignKey(
                name: "fk_jobs__mrp_planned_orders_mrp_planned_order_id",
                table: "jobs",
                column: "mrp_planned_order_id",
                principalTable: "mrp_planned_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_order_lines_mrp_planned_orders_mrp_planned_order_id",
                table: "purchase_order_lines",
                column: "mrp_planned_order_id",
                principalTable: "mrp_planned_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_jobs__mrp_planned_orders_mrp_planned_order_id",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_order_lines_mrp_planned_orders_mrp_planned_order_id",
                table: "purchase_order_lines");

            migrationBuilder.DropTable(
                name: "mrp_demands");

            migrationBuilder.DropTable(
                name: "mrp_exceptions");

            migrationBuilder.DropTable(
                name: "mrp_supplies");

            migrationBuilder.DropTable(
                name: "mrp_planned_orders");

            migrationBuilder.DropTable(
                name: "mrp_runs");

            migrationBuilder.DropIndex(
                name: "ix_purchase_order_lines_mrp_planned_order_id",
                table: "purchase_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_jobs_mrp_planned_order_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "mrp_planned_order_id",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "demand_fence_days",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "fixed_order_quantity",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "is_mrp_planned",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "lot_sizing_rule",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "minimum_order_quantity",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "order_multiple",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "planning_fence_days",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "mrp_planned_order_id",
                table: "jobs");
        }
    }
}
