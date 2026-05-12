using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWave7to9Entities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_follow_up_tasks_assigned_to_user_id_status",
                table: "follow_up_tasks");

            migrationBuilder.DropIndex(
                name: "ix_domain_event_failures_status",
                table: "domain_event_failures");

            migrationBuilder.AddColumn<string>(
                name: "error_message",
                table: "training_scan_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "was_successful",
                table: "training_scan_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "trigger_type",
                table: "follow_up_tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "follow_up_tasks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "follow_up_tasks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_all_day",
                table: "events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_system_generated",
                table: "events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "domain_event_failures",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "scan_action_log_id",
                table: "bin_movements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scan_action_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    action_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    part_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    from_location_id = table.Column<int>(type: "integer", nullable: true),
                    to_location_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    related_entity_id = table.Column<int>(type: "integer", nullable: true),
                    related_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reversed_by_log_id = table.Column<int>(type: "integer", nullable: true),
                    reverses_log_id = table.Column<int>(type: "integer", nullable: true),
                    is_reversed = table.Column<bool>(type: "boolean", nullable: false),
                    is_training_mode = table.Column<bool>(type: "boolean", nullable: false),
                    kiosk_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    device_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_action_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_scan_action_logs__storage_locations_from_location_id",
                        column: x => x.from_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scan_action_logs__storage_locations_to_location_id",
                        column: x => x.to_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scan_action_logs_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "schedule_milestones",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sales_order_line_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    milestone_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actual_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_schedule_milestones_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_schedule_milestones_sales_order_lines_sales_order_line_id",
                        column: x => x.sales_order_line_id,
                        principalTable: "sales_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_assigned_to_user_id",
                table: "follow_up_tasks",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_due_date",
                table: "follow_up_tasks",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_status",
                table: "follow_up_tasks",
                column: "status",
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_trigger_type",
                table: "follow_up_tasks",
                column: "trigger_type");

            migrationBuilder.CreateIndex(
                name: "ix_domain_event_failures_event_type",
                table: "domain_event_failures",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_domain_event_failures_failed_at",
                table: "domain_event_failures",
                column: "failed_at");

            migrationBuilder.CreateIndex(
                name: "ix_domain_event_failures_status",
                table: "domain_event_failures",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_bin_movements_scan_action_log_id",
                table: "bin_movements",
                column: "scan_action_log_id",
                filter: "scan_action_log_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_action_type",
                table: "scan_action_logs",
                column: "action_type");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_created_at",
                table: "scan_action_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_from_location_id",
                table: "scan_action_logs",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_part_id",
                table: "scan_action_logs",
                column: "part_id",
                filter: "part_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_reversed_by_log_id",
                table: "scan_action_logs",
                column: "reversed_by_log_id",
                filter: "reversed_by_log_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_reverses_log_id",
                table: "scan_action_logs",
                column: "reverses_log_id",
                filter: "reverses_log_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_to_location_id",
                table: "scan_action_logs",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_user_id",
                table: "scan_action_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_milestones_job_id",
                table: "schedule_milestones",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_milestones_milestone_type",
                table: "schedule_milestones",
                column: "milestone_type");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_milestones_sales_order_line_id",
                table: "schedule_milestones",
                column: "sales_order_line_id");

            migrationBuilder.AddForeignKey(
                name: "fk_bin_movements__scan_action_logs_scan_action_log_id",
                table: "bin_movements",
                column: "scan_action_log_id",
                principalTable: "scan_action_logs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bin_movements__scan_action_logs_scan_action_log_id",
                table: "bin_movements");

            migrationBuilder.DropTable(
                name: "scan_action_logs");

            migrationBuilder.DropTable(
                name: "schedule_milestones");

            migrationBuilder.DropIndex(
                name: "ix_follow_up_tasks_assigned_to_user_id",
                table: "follow_up_tasks");

            migrationBuilder.DropIndex(
                name: "ix_follow_up_tasks_due_date",
                table: "follow_up_tasks");

            migrationBuilder.DropIndex(
                name: "ix_follow_up_tasks_status",
                table: "follow_up_tasks");

            migrationBuilder.DropIndex(
                name: "ix_follow_up_tasks_trigger_type",
                table: "follow_up_tasks");

            migrationBuilder.DropIndex(
                name: "ix_domain_event_failures_event_type",
                table: "domain_event_failures");

            migrationBuilder.DropIndex(
                name: "ix_domain_event_failures_failed_at",
                table: "domain_event_failures");

            migrationBuilder.DropIndex(
                name: "ix_domain_event_failures_status",
                table: "domain_event_failures");

            migrationBuilder.DropIndex(
                name: "ix_bin_movements_scan_action_log_id",
                table: "bin_movements");

            migrationBuilder.DropColumn(
                name: "error_message",
                table: "training_scan_logs");

            migrationBuilder.DropColumn(
                name: "was_successful",
                table: "training_scan_logs");

            migrationBuilder.DropColumn(
                name: "is_all_day",
                table: "events");

            migrationBuilder.DropColumn(
                name: "is_system_generated",
                table: "events");

            migrationBuilder.DropColumn(
                name: "scan_action_log_id",
                table: "bin_movements");

            migrationBuilder.AlterColumn<string>(
                name: "trigger_type",
                table: "follow_up_tasks",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "follow_up_tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "follow_up_tasks",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "domain_event_failures",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_assigned_to_user_id_status",
                table: "follow_up_tasks",
                columns: new[] { "assigned_to_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_domain_event_failures_status",
                table: "domain_event_failures",
                column: "status",
                filter: "status != 'Resolved'");
        }
    }
}
