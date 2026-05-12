using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOeeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "actual_cycle_time_seconds",
                table: "production_runs",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ideal_cycle_time_seconds",
                table: "production_runs",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rework_quantity",
                table: "production_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "work_center_id",
                table: "production_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "downtime_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "downtime_logs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "downtime_reason_id",
                table: "downtime_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "job_id",
                table: "downtime_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "work_center_id",
                table: "downtime_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_runs_work_center_id",
                table: "production_runs",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_downtime_logs_job_id",
                table: "downtime_logs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_downtime_logs_work_center_id",
                table: "downtime_logs",
                column: "work_center_id");

            migrationBuilder.AddForeignKey(
                name: "fk_downtime_logs__jobs_job_id",
                table: "downtime_logs",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_downtime_logs__work_centers_work_center_id",
                table: "downtime_logs",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_production_runs__work_centers_work_center_id",
                table: "production_runs",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_downtime_logs__jobs_job_id",
                table: "downtime_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_downtime_logs__work_centers_work_center_id",
                table: "downtime_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_production_runs__work_centers_work_center_id",
                table: "production_runs");

            migrationBuilder.DropIndex(
                name: "ix_production_runs_work_center_id",
                table: "production_runs");

            migrationBuilder.DropIndex(
                name: "ix_downtime_logs_job_id",
                table: "downtime_logs");

            migrationBuilder.DropIndex(
                name: "ix_downtime_logs_work_center_id",
                table: "downtime_logs");

            migrationBuilder.DropColumn(
                name: "actual_cycle_time_seconds",
                table: "production_runs");

            migrationBuilder.DropColumn(
                name: "ideal_cycle_time_seconds",
                table: "production_runs");

            migrationBuilder.DropColumn(
                name: "rework_quantity",
                table: "production_runs");

            migrationBuilder.DropColumn(
                name: "work_center_id",
                table: "production_runs");

            migrationBuilder.DropColumn(
                name: "category",
                table: "downtime_logs");

            migrationBuilder.DropColumn(
                name: "description",
                table: "downtime_logs");

            migrationBuilder.DropColumn(
                name: "downtime_reason_id",
                table: "downtime_logs");

            migrationBuilder.DropColumn(
                name: "job_id",
                table: "downtime_logs");

            migrationBuilder.DropColumn(
                name: "work_center_id",
                table: "downtime_logs");
        }
    }
}
