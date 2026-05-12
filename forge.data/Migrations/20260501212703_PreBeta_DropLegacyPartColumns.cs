using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <summary>
    /// Pre-beta cleanup: drops the four legacy Part columns
    /// (<c>part_type</c>, <c>is_serial_tracked</c>, <c>material</c>,
    /// <c>mold_tool_ref</c>) that Pillar 1 / Pillar 2 retired in favour of
    /// the three orthogonal type axes, the <c>traceability_type</c> enum,
    /// the <c>material_spec_id</c> FK, and the <c>tooling_asset_id</c> FK
    /// respectively. We held them for two release cycles for rollback
    /// safety, but pre-beta means there is no production data to preserve.
    ///
    /// The Down migration restores the columns as nullable / default-zero
    /// — best-effort only. Existing axis values are NOT projected back into
    /// the legacy single-axis enum. Don't run Down in anger; it's purely
    /// defensive scaffolding.
    /// </summary>
    public partial class PreBeta_DropLegacyPartColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_serial_tracked",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "material",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "mold_tool_ref",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "part_type",
                table: "parts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort restore. We can't reconstitute the original
            // values from the three axes / TraceabilityType / MaterialSpec FK
            // — re-add the columns as defaultable so legacy code compiling
            // against an old snapshot can at least open the table.
            migrationBuilder.AddColumn<bool>(
                name: "is_serial_tracked",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "material",
                table: "parts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mold_tool_ref",
                table: "parts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "part_type",
                table: "parts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
