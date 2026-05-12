using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCostingEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "burden_cost",
                table: "time_entries",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "labor_cost",
                table: "time_entries",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "operation_id",
                table: "time_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "burden_rate",
                table: "operations",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_burden_cost",
                table: "operations",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_labor_cost",
                table: "operations",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "labor_rate",
                table: "operations",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_burden_cost",
                table: "jobs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_labor_cost",
                table: "jobs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_material_cost",
                table: "jobs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_subcontract_cost",
                table: "jobs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "quoted_price",
                table: "jobs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "labor_rates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    standard_rate_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    overtime_rate_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    doubletime_rate_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labor_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_labor_rates__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "material_issues",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    issued_by_id = table.Column<int>(type: "integer", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bin_content_id = table.Column<int>(type: "integer", nullable: true),
                    storage_location_id = table.Column<int>(type: "integer", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issue_type = table.Column<int>(type: "integer", nullable: false),
                    return_reason_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_issues", x => x.id);
                    table.ForeignKey(
                        name: "fk_material_issues__asp_net_users_issued_by_id",
                        column: x => x.issued_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_material_issues__operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_material_issues__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_material_issues__storage_locations_storage_location_id",
                        column: x => x.storage_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_material_issues_bin_contents_bin_content_id",
                        column: x => x.bin_content_id,
                        principalTable: "bin_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_material_issues_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_operation_id",
                table: "time_entries",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_labor_rates_user_id_effective_from",
                table: "labor_rates",
                columns: new[] { "user_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_bin_content_id",
                table: "material_issues",
                column: "bin_content_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_issued_by_id",
                table: "material_issues",
                column: "issued_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_job_id",
                table: "material_issues",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_operation_id",
                table: "material_issues",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_part_id",
                table: "material_issues",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_storage_location_id",
                table: "material_issues",
                column: "storage_location_id");

            migrationBuilder.AddForeignKey(
                name: "fk_time_entries_operations_operation_id",
                table: "time_entries",
                column: "operation_id",
                principalTable: "operations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_time_entries_operations_operation_id",
                table: "time_entries");

            migrationBuilder.DropTable(
                name: "labor_rates");

            migrationBuilder.DropTable(
                name: "material_issues");

            migrationBuilder.DropIndex(
                name: "ix_time_entries_operation_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "burden_cost",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "labor_cost",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "operation_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "burden_rate",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "estimated_burden_cost",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "estimated_labor_cost",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "labor_rate",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "estimated_burden_cost",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "estimated_labor_cost",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "estimated_material_cost",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "estimated_subcontract_cost",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "quoted_price",
                table: "jobs");
        }
    }
}
