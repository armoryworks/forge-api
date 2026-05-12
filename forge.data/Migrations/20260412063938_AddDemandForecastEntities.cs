using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDemandForecastEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demand_forecasts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    historical_periods = table.Column<int>(type: "integer", nullable: false),
                    forecast_periods = table.Column<int>(type: "integer", nullable: false),
                    smoothing_factor = table.Column<double>(type: "double precision", nullable: true),
                    forecast_start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    forecast_data_json = table.Column<string>(type: "jsonb", nullable: true),
                    applied_to_master_schedule_id = table.Column<int>(type: "integer", nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demand_forecasts", x => x.id);
                    table.ForeignKey(
                        name: "fk_demand_forecasts__master_schedules_applied_to_master_schedule~",
                        column: x => x.applied_to_master_schedule_id,
                        principalTable: "master_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_demand_forecasts__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "forecast_overrides",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    demand_forecast_id = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    override_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    overridden_by_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forecast_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_forecast_overrides_demand_forecasts_demand_forecast_id",
                        column: x => x.demand_forecast_id,
                        principalTable: "demand_forecasts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_applied_to_master_schedule_id",
                table: "demand_forecasts",
                column: "applied_to_master_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_created_by_user_id",
                table: "demand_forecasts",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_part_id",
                table: "demand_forecasts",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_status",
                table: "demand_forecasts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_overrides_demand_forecast_id",
                table: "forecast_overrides",
                column: "demand_forecast_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forecast_overrides");

            migrationBuilder.DropTable(
                name: "demand_forecasts");
        }
    }
}
