using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_DiscoveryRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false),
                    recommended_preset_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    applied_preset_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    recommended_confidence = table.Column<double>(type: "double precision", nullable: false),
                    applied_deltas_json = table.Column<string>(type: "jsonb", nullable: false),
                    ran_in_consultant_mode = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_runs_applied_preset_id",
                table: "discovery_runs",
                column: "applied_preset_id");

            migrationBuilder.CreateIndex(
                name: "ix_discovery_runs_completed_at",
                table: "discovery_runs",
                column: "completed_at");

            migrationBuilder.CreateIndex(
                name: "ix_discovery_runs_run_by_user_id",
                table: "discovery_runs",
                column: "run_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovery_runs");
        }
    }
}
