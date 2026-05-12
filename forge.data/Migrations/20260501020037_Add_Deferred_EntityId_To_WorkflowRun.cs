using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_Deferred_EntityId_To_WorkflowRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workflow_runs_entity_type_entity_id",
                table: "workflow_runs");

            migrationBuilder.AlterColumn<int>(
                name: "entity_id",
                table: "workflow_runs",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "draft_payload",
                table: "workflow_runs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_entity_type_entity_id",
                table: "workflow_runs",
                columns: new[] { "entity_type", "entity_id" },
                unique: true,
                filter: "\"entity_id\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workflow_runs_entity_type_entity_id",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "draft_payload",
                table: "workflow_runs");

            migrationBuilder.AlterColumn<int>(
                name: "entity_id",
                table: "workflow_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_entity_type_entity_id",
                table: "workflow_runs",
                columns: new[] { "entity_type", "entity_id" },
                unique: true);
        }
    }
}
