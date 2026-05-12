using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobEngagementAxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "billing_model",
                table: "jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "engagement_type_id",
                table: "jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "project_phase_id",
                table: "jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "retainer_balance_hours",
                table: "jobs",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "retainer_hours",
                table: "jobs",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sow_id",
                table: "jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_engagement_type_id",
                table: "jobs",
                column: "engagement_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_project_phase_id",
                table: "jobs",
                column: "project_phase_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_sow_id",
                table: "jobs",
                column: "sow_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_jobs_engagement_type_id",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_project_phase_id",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_sow_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "billing_model",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "engagement_type_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "project_phase_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "retainer_balance_hours",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "retainer_hours",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "sow_id",
                table: "jobs");
        }
    }
}
