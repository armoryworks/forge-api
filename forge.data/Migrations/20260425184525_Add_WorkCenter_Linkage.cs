using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_WorkCenter_Linkage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "work_center_id",
                table: "time_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "operation_id",
                table: "status_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "work_center_id",
                table: "status_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "work_center_id",
                table: "kiosk_terminals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "operation_id",
                table: "job_activity_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "work_center_id",
                table: "job_activity_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "work_center_qualifications",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    qualified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    qualified_by_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_center_qualifications", x => new { x.user_id, x.work_center_id });
                    table.ForeignKey(
                        name: "fk_work_center_qualifications__asp_net_users_qualified_by_id",
                        column: x => x.qualified_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_work_center_qualifications__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_work_center_qualifications_work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_work_center_id",
                table: "time_entries",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_status_entries_operation_id",
                table: "status_entries",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_status_entries_work_center_id",
                table: "status_entries",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_terminals_work_center_id",
                table: "kiosk_terminals",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_activity_logs_operation_id",
                table: "job_activity_logs",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_activity_logs_work_center_id",
                table: "job_activity_logs",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_center_qualifications_qualified_by_id",
                table: "work_center_qualifications",
                column: "qualified_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_center_qualifications_work_center_id",
                table: "work_center_qualifications",
                column: "work_center_id");

            migrationBuilder.AddForeignKey(
                name: "fk_job_activity_logs__operations_operation_id",
                table: "job_activity_logs",
                column: "operation_id",
                principalTable: "operations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_job_activity_logs__work_centers_work_center_id",
                table: "job_activity_logs",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_kiosk_terminals__work_centers_work_center_id",
                table: "kiosk_terminals",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_status_entries__work_centers_work_center_id",
                table: "status_entries",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_status_entries_operations_operation_id",
                table: "status_entries",
                column: "operation_id",
                principalTable: "operations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_time_entries__work_centers_work_center_id",
                table: "time_entries",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_job_activity_logs__operations_operation_id",
                table: "job_activity_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_job_activity_logs__work_centers_work_center_id",
                table: "job_activity_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_kiosk_terminals__work_centers_work_center_id",
                table: "kiosk_terminals");

            migrationBuilder.DropForeignKey(
                name: "fk_status_entries__work_centers_work_center_id",
                table: "status_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_status_entries_operations_operation_id",
                table: "status_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_time_entries__work_centers_work_center_id",
                table: "time_entries");

            migrationBuilder.DropTable(
                name: "work_center_qualifications");

            migrationBuilder.DropIndex(
                name: "ix_time_entries_work_center_id",
                table: "time_entries");

            migrationBuilder.DropIndex(
                name: "ix_status_entries_operation_id",
                table: "status_entries");

            migrationBuilder.DropIndex(
                name: "ix_status_entries_work_center_id",
                table: "status_entries");

            migrationBuilder.DropIndex(
                name: "ix_kiosk_terminals_work_center_id",
                table: "kiosk_terminals");

            migrationBuilder.DropIndex(
                name: "ix_job_activity_logs_operation_id",
                table: "job_activity_logs");

            migrationBuilder.DropIndex(
                name: "ix_job_activity_logs_work_center_id",
                table: "job_activity_logs");

            migrationBuilder.DropColumn(
                name: "work_center_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "operation_id",
                table: "status_entries");

            migrationBuilder.DropColumn(
                name: "work_center_id",
                table: "status_entries");

            migrationBuilder.DropColumn(
                name: "work_center_id",
                table: "kiosk_terminals");

            migrationBuilder.DropColumn(
                name: "operation_id",
                table: "job_activity_logs");

            migrationBuilder.DropColumn(
                name: "work_center_id",
                table: "job_activity_logs");
        }
    }
}
