using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameBomEntryToBomLine : Migration
    {
        // Data-preserving rename of "BOM entry" -> "BOM line", covering both the
        // live BOMEntry table ("bomentries") and the frozen-revision sibling
        // BomRevisionEntry table ("bom_revision_entries"), plus the FK column
        // operation_materials.bom_entry_id. Hand-authored so rows are preserved
        // (rename, not drop+create). PK/FK constraint renames use raw SQL.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- bomentries -> bomlines ------------------------------------
            migrationBuilder.RenameTable(name: "bomentries", newName: "bomlines");
            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT pk_bomentries TO pk_bomlines;");

            migrationBuilder.RenameIndex(name: "ix_bomentries_child_part_id", table: "bomlines", newName: "ix_bomlines_child_part_id");
            migrationBuilder.RenameIndex(name: "ix_bomentries_parent_part_id", table: "bomlines", newName: "ix_bomlines_parent_part_id");
            migrationBuilder.RenameIndex(name: "ix_bomentries_uom_id", table: "bomlines", newName: "ix_bomlines_uom_id");
            migrationBuilder.RenameIndex(name: "ix_bomentries_vendor_id", table: "bomlines", newName: "ix_bomlines_vendor_id");

            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT \"fk_bomentries__parts_child_part_id\" TO \"fk_bomlines__parts_child_part_id\";");
            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT \"fk_bomentries__parts_parent_part_id\" TO \"fk_bomlines__parts_parent_part_id\";");
            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT \"fk_bomentries__units_of_measure_uom_id\" TO \"fk_bomlines__units_of_measure_uom_id\";");
            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT \"fk_bomentries__vendors_vendor_id\" TO \"fk_bomlines__vendors_vendor_id\";");

            // ---- bom_revision_entries -> bom_revision_lines ----------------
            migrationBuilder.RenameTable(name: "bom_revision_entries", newName: "bom_revision_lines");
            migrationBuilder.Sql("ALTER TABLE bom_revision_lines RENAME CONSTRAINT pk_bom_revision_entries TO pk_bom_revision_lines;");

            migrationBuilder.RenameIndex(name: "ix_bom_revision_entries_bom_revision_id", table: "bom_revision_lines", newName: "ix_bom_revision_lines_bom_revision_id");
            migrationBuilder.RenameIndex(name: "ix_bom_revision_entries_part_id", table: "bom_revision_lines", newName: "ix_bom_revision_lines_part_id");

            migrationBuilder.Sql("ALTER TABLE bom_revision_lines RENAME CONSTRAINT fk_bom_revision_entries_bom_revisions_bom_revision_id TO fk_bom_revision_lines_bom_revisions_bom_revision_id;");
            migrationBuilder.Sql("ALTER TABLE bom_revision_lines RENAME CONSTRAINT \"fk_bom_revision_entries__parts_part_id\" TO \"fk_bom_revision_lines__parts_part_id\";");

            // ---- operation_materials.bom_entry_id -> bom_line_id -----------
            migrationBuilder.RenameColumn(name: "bom_entry_id", table: "operation_materials", newName: "bom_line_id");
            migrationBuilder.RenameIndex(name: "ix_operation_materials_bom_entry_id", table: "operation_materials", newName: "ix_operation_materials_bom_line_id");
            migrationBuilder.Sql("ALTER TABLE operation_materials RENAME CONSTRAINT fk_operation_materials_bomentries_bom_entry_id TO fk_operation_materials_bomlines_bom_line_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE operation_materials RENAME CONSTRAINT fk_operation_materials_bomlines_bom_line_id TO fk_operation_materials_bomentries_bom_entry_id;");
            migrationBuilder.RenameIndex(name: "ix_operation_materials_bom_line_id", table: "operation_materials", newName: "ix_operation_materials_bom_entry_id");
            migrationBuilder.RenameColumn(name: "bom_line_id", table: "operation_materials", newName: "bom_entry_id");

            migrationBuilder.Sql("ALTER TABLE bom_revision_lines RENAME CONSTRAINT \"fk_bom_revision_lines__parts_part_id\" TO \"fk_bom_revision_entries__parts_part_id\";");
            migrationBuilder.Sql("ALTER TABLE bom_revision_lines RENAME CONSTRAINT fk_bom_revision_lines_bom_revisions_bom_revision_id TO fk_bom_revision_entries_bom_revisions_bom_revision_id;");
            migrationBuilder.RenameIndex(name: "ix_bom_revision_lines_part_id", table: "bom_revision_entries", newName: "ix_bom_revision_entries_part_id");
            migrationBuilder.RenameIndex(name: "ix_bom_revision_lines_bom_revision_id", table: "bom_revision_entries", newName: "ix_bom_revision_entries_bom_revision_id");
            migrationBuilder.Sql("ALTER TABLE bom_revision_lines RENAME CONSTRAINT pk_bom_revision_lines TO pk_bom_revision_entries;");
            migrationBuilder.RenameTable(name: "bom_revision_lines", newName: "bom_revision_entries");

            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT \"fk_bomlines__vendors_vendor_id\" TO \"fk_bomentries__vendors_vendor_id\";");
            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT \"fk_bomlines__units_of_measure_uom_id\" TO \"fk_bomentries__units_of_measure_uom_id\";");
            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT \"fk_bomlines__parts_parent_part_id\" TO \"fk_bomentries__parts_parent_part_id\";");
            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT \"fk_bomlines__parts_child_part_id\" TO \"fk_bomentries__parts_child_part_id\";");
            migrationBuilder.RenameIndex(name: "ix_bomlines_vendor_id", table: "bomentries", newName: "ix_bomentries_vendor_id");
            migrationBuilder.RenameIndex(name: "ix_bomlines_uom_id", table: "bomentries", newName: "ix_bomentries_uom_id");
            migrationBuilder.RenameIndex(name: "ix_bomlines_parent_part_id", table: "bomentries", newName: "ix_bomentries_parent_part_id");
            migrationBuilder.RenameIndex(name: "ix_bomlines_child_part_id", table: "bomentries", newName: "ix_bomentries_child_part_id");
            migrationBuilder.Sql("ALTER TABLE bomlines RENAME CONSTRAINT pk_bomlines TO pk_bomentries;");
            migrationBuilder.RenameTable(name: "bomlines", newName: "bomentries");
        }
    }
}
