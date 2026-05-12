using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_WorkflowPattern_Substrate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "current_cost_calculation_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "manual_cost_override",
                table: "parts",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "costing_profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    flat_rate_pct = table.Column<decimal>(type: "numeric(7,4)", nullable: true),
                    departmental_rates = table.Column<string>(type: "jsonb", nullable: true),
                    pools = table.Column<string>(type: "jsonb", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costing_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entity_readiness_validators",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    validator_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    predicate = table.Column<string>(type: "jsonb", nullable: false),
                    display_name_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    missing_message_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_seed_data = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_readiness_validators", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    default_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    steps_json = table.Column<string>(type: "jsonb", nullable: false),
                    express_template_component = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_seed_data = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    current_step_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandoned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandoned_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cost_calculations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    profile_id = table.Column<int>(type: "integer", nullable: false),
                    profile_version = table.Column<int>(type: "integer", nullable: false),
                    result_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    result_breakdown = table.Column<string>(type: "jsonb", nullable: true),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calculated_by = table.Column<int>(type: "integer", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_calculations", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_calculations__costing_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "costing_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_run_entities",
                columns: table => new
                {
                    run_id = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_run_entities", x => new { x.run_id, x.entity_type, x.entity_id });
                    table.ForeignKey(
                        name: "fk_workflow_run_entities_workflow_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cost_calculation_inputs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cost_calculation_id = table.Column<int>(type: "integer", nullable: false),
                    direct_material_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    direct_labor_hours = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    direct_labor_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    machine_hours = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    overhead_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    overhead_rate_pct = table.Column<decimal>(type: "numeric(7,4)", nullable: true),
                    custom_inputs = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_calculation_inputs", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_calculation_inputs_cost_calculations_cost_calculation_~",
                        column: x => x.cost_calculation_id,
                        principalTable: "cost_calculations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parts_current_cost_calculation_id",
                table: "parts",
                column: "current_cost_calculation_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_calculation_inputs_cost_calculation_id",
                table: "cost_calculation_inputs",
                column: "cost_calculation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_calculations_entity_type_entity_id",
                table: "cost_calculations",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cost_calculations_is_current",
                table: "cost_calculations",
                column: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_cost_calculations_profile_id",
                table: "cost_calculations",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_costing_profiles_code",
                table: "costing_profiles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_readiness_validators_entity_type",
                table: "entity_readiness_validators",
                column: "entity_type");

            migrationBuilder.CreateIndex(
                name: "ix_entity_readiness_validators_entity_type_validator_id",
                table: "entity_readiness_validators",
                columns: new[] { "entity_type", "validator_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_definition_id",
                table: "workflow_definitions",
                column: "definition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_entity_type",
                table: "workflow_definitions",
                column: "entity_type");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_run_entities_entity_type_entity_id",
                table: "workflow_run_entities",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_definition_id",
                table: "workflow_runs",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_entity_type_entity_id",
                table: "workflow_runs",
                columns: new[] { "entity_type", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_last_activity_at",
                table: "workflow_runs",
                column: "last_activity_at");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_started_by_user_id",
                table: "workflow_runs",
                column: "started_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_parts_cost_calculations_current_cost_calculation_id",
                table: "parts",
                column: "current_cost_calculation_id",
                principalTable: "cost_calculations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_parts_cost_calculations_current_cost_calculation_id",
                table: "parts");

            migrationBuilder.DropTable(
                name: "cost_calculation_inputs");

            migrationBuilder.DropTable(
                name: "entity_readiness_validators");

            migrationBuilder.DropTable(
                name: "workflow_definitions");

            migrationBuilder.DropTable(
                name: "workflow_run_entities");

            migrationBuilder.DropTable(
                name: "cost_calculations");

            migrationBuilder.DropTable(
                name: "workflow_runs");

            migrationBuilder.DropTable(
                name: "costing_profiles");

            migrationBuilder.DropIndex(
                name: "ix_parts_current_cost_calculation_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "current_cost_calculation_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "manual_cost_override",
                table: "parts");
        }
    }
}
