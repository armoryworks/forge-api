using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_operations_assets_work_center_id",
                table: "operations");

            migrationBuilder.AddColumn<int>(
                name: "asset_id",
                table: "operations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_subcontract",
                table: "operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "overlap_percent",
                table: "operations",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "run_minutes_each",
                table: "operations",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "run_minutes_lot",
                table: "operations",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "scrap_factor",
                table: "operations",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "setup_minutes",
                table: "operations",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "subcontract_cost",
                table: "operations",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "subcontract_vendor_id",
                table: "operations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "schedule_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    operations_scheduled = table.Column<int>(type: "integer", nullable: false),
                    conflicts_detected = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    net_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shifts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_centers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    company_location_id = table.Column<int>(type: "integer", nullable: true),
                    asset_id = table.Column<int>(type: "integer", nullable: true),
                    daily_capacity_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    efficiency_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    number_of_machines = table.Column<int>(type: "integer", nullable: false),
                    labor_cost_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    burden_rate_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ideal_cycle_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_centers", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_centers_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_work_centers_company_locations_company_location_id",
                        column: x => x.company_location_id,
                        principalTable: "company_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_operations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    setup_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    setup_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    setup_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    run_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    total_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    schedule_run_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_operations__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scheduled_operations_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scheduled_operations_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scheduled_operations_schedule_runs_schedule_run_id",
                        column: x => x.schedule_run_id,
                        principalTable: "schedule_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "work_center_calendars",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    available_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_center_calendars", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_center_calendars_work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_center_shifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    shift_id = table.Column<int>(type: "integer", nullable: false),
                    days_of_week = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_center_shifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_center_shifts_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_work_center_shifts_work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_operations_asset_id",
                table: "operations",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_operations_subcontract_vendor_id",
                table: "operations",
                column: "subcontract_vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_job_id",
                table: "scheduled_operations",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_operation_id",
                table: "scheduled_operations",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_schedule_run_id",
                table: "scheduled_operations",
                column: "schedule_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_work_center_id",
                table: "scheduled_operations",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_work_center_id_scheduled_start",
                table: "scheduled_operations",
                columns: new[] { "work_center_id", "scheduled_start" });

            migrationBuilder.CreateIndex(
                name: "ix_work_center_calendars_work_center_id_date",
                table: "work_center_calendars",
                columns: new[] { "work_center_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_center_shifts_shift_id",
                table: "work_center_shifts",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_center_shifts_work_center_id_shift_id",
                table: "work_center_shifts",
                columns: new[] { "work_center_id", "shift_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_centers_asset_id",
                table: "work_centers",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_centers_code",
                table: "work_centers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_centers_company_location_id",
                table: "work_centers",
                column: "company_location_id");

            migrationBuilder.AddForeignKey(
                name: "fk_operations__vendors_subcontract_vendor_id",
                table: "operations",
                column: "subcontract_vendor_id",
                principalTable: "vendors",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_operations__work_centers_work_center_id",
                table: "operations",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_operations_assets_asset_id",
                table: "operations",
                column: "asset_id",
                principalTable: "assets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_operations__vendors_subcontract_vendor_id",
                table: "operations");

            migrationBuilder.DropForeignKey(
                name: "fk_operations__work_centers_work_center_id",
                table: "operations");

            migrationBuilder.DropForeignKey(
                name: "fk_operations_assets_asset_id",
                table: "operations");

            migrationBuilder.DropTable(
                name: "scheduled_operations");

            migrationBuilder.DropTable(
                name: "work_center_calendars");

            migrationBuilder.DropTable(
                name: "work_center_shifts");

            migrationBuilder.DropTable(
                name: "schedule_runs");

            migrationBuilder.DropTable(
                name: "shifts");

            migrationBuilder.DropTable(
                name: "work_centers");

            migrationBuilder.DropIndex(
                name: "ix_operations_asset_id",
                table: "operations");

            migrationBuilder.DropIndex(
                name: "ix_operations_subcontract_vendor_id",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "asset_id",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "is_subcontract",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "overlap_percent",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "run_minutes_each",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "run_minutes_lot",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "scrap_factor",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "setup_minutes",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "subcontract_cost",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "subcontract_vendor_id",
                table: "operations");

            migrationBuilder.AddForeignKey(
                name: "fk_operations_assets_work_center_id",
                table: "operations",
                column: "work_center_id",
                principalTable: "assets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
