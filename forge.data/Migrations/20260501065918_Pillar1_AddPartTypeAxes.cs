using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Pillar1_AddPartTypeAxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "abc_class",
                table: "parts",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inventory_class",
                table: "parts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Component");

            migrationBuilder.AddColumn<int>(
                name: "item_kind_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manufacturer_name",
                table: "parts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manufacturer_part_number",
                table: "parts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "procurement_source",
                table: "parts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Buy");

            migrationBuilder.AddColumn<string>(
                name: "traceability_type",
                table: "parts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            // Pillar 1 — backfill the new axes from legacy `part_type` per the
            // audit's Section 9 mapping. Heuristic for `Part` (the catch-all):
            // BOMEntries-present ⇒ Make+Subassembly; otherwise the Buy+Component
            // default already in place. Best-effort only — no edge-case chasing
            // (Open Decision #8).
            //
            // NB: PartType is stored as INTEGER in Postgres (EF default; no
            // `.HasConversion<string>()` configured on the legacy enum). So
            // the CASE matches ordinal values, not strings:
            //   Part=0, Assembly=1, RawMaterial=2, Consumable=3, Tooling=4,
            //   Fastener=5, Electronic=6, Packaging=7.
            migrationBuilder.Sql("""
                UPDATE parts SET
                    procurement_source = CASE part_type
                        WHEN 1 THEN 'Make'   -- Assembly
                        WHEN 2 THEN 'Buy'    -- RawMaterial
                        WHEN 3 THEN 'Buy'    -- Consumable
                        WHEN 4 THEN CASE WHEN tooling_asset_id IS NOT NULL THEN 'Make' ELSE 'Buy' END  -- Tooling
                        WHEN 5 THEN 'Buy'    -- Fastener
                        WHEN 6 THEN 'Buy'    -- Electronic
                        WHEN 7 THEN 'Buy'    -- Packaging
                        ELSE 'Buy'           -- Part (catch-all)
                    END,
                    inventory_class = CASE part_type
                        WHEN 1 THEN 'Subassembly'  -- Assembly
                        WHEN 2 THEN 'Raw'          -- RawMaterial
                        WHEN 3 THEN 'Consumable'   -- Consumable
                        WHEN 4 THEN 'Tool'         -- Tooling
                        WHEN 5 THEN 'Component'    -- Fastener
                        WHEN 6 THEN 'Component'    -- Electronic
                        WHEN 7 THEN 'Consumable'   -- Packaging
                        ELSE 'Component'           -- Part (catch-all)
                    END,
                    traceability_type = CASE WHEN is_serial_tracked = true THEN 'Serial' ELSE 'None' END;
            """);

            // Catch-all `Part` rows (ordinal 0) with a non-empty BOM are very
            // likely in-house subassemblies. Promote them post-default.
            // Table name is `bomentries` (snake_case converter doesn't split
            // the all-caps BOM prefix from the rest of BOMEntries).
            migrationBuilder.Sql("""
                UPDATE parts p SET
                    procurement_source = 'Make',
                    inventory_class = 'Subassembly'
                WHERE p.part_type = 0
                  AND EXISTS (SELECT 1 FROM bomentries b WHERE b.parent_part_id = p.id AND b.deleted_at IS NULL);
            """);

            migrationBuilder.CreateIndex(
                name: "ix_parts_item_kind_id",
                table: "parts",
                column: "item_kind_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_manufacturer_part_number",
                table: "parts",
                column: "manufacturer_part_number");

            migrationBuilder.CreateIndex(
                name: "ix_parts_procurement_source_inventory_class",
                table: "parts",
                columns: new[] { "procurement_source", "inventory_class" });

            migrationBuilder.AddForeignKey(
                name: "fk_parts__reference_data_item_kind_id",
                table: "parts",
                column: "item_kind_id",
                principalTable: "reference_data",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_parts__reference_data_item_kind_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_parts_item_kind_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_parts_manufacturer_part_number",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_parts_procurement_source_inventory_class",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "abc_class",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "inventory_class",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "item_kind_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "manufacturer_name",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "manufacturer_part_number",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "procurement_source",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "traceability_type",
                table: "parts");
        }
    }
}
