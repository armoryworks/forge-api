using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePurchaseOptionToPurchaseUnit : Migration
    {
        // Data-preserving rename of "purchase option" -> "purchase unit".
        // Hand-authored (not ef-scaffolded) so the table/columns are RENAMED
        // rather than dropped+recreated. PK/FK constraint renames use raw SQL
        // because MigrationBuilder has no typed constraint-rename op.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Rename the table.
            migrationBuilder.RenameTable(
                name: "part_purchase_options",
                newName: "part_purchase_units");

            // 2. Rename the table's primary key constraint.
            migrationBuilder.Sql(
                "ALTER TABLE part_purchase_units RENAME CONSTRAINT pk_part_purchase_options TO pk_part_purchase_units;");

            // 3. Rename the table's own indexes.
            migrationBuilder.RenameIndex(
                name: "ix_part_purchase_options_content_uom_id",
                table: "part_purchase_units",
                newName: "ix_part_purchase_units_content_uom_id");
            migrationBuilder.RenameIndex(
                name: "ix_part_purchase_options_part_id",
                table: "part_purchase_units",
                newName: "ix_part_purchase_units_part_id");

            // 4. Rename the table's own foreign-key constraints.
            migrationBuilder.Sql(
                "ALTER TABLE part_purchase_units RENAME CONSTRAINT \"fk_part_purchase_options__units_of_measure_content_uom_id\" TO \"fk_part_purchase_units__units_of_measure_content_uom_id\";");
            migrationBuilder.Sql(
                "ALTER TABLE part_purchase_units RENAME CONSTRAINT fk_part_purchase_options_parts_part_id TO fk_part_purchase_units_parts_part_id;");

            // 5. Rename the FK columns on the referencing tables.
            migrationBuilder.RenameColumn(
                name: "purchase_option_id",
                table: "purchase_order_lines",
                newName: "purchase_unit_id");
            migrationBuilder.RenameColumn(
                name: "purchase_option_id",
                table: "vendor_part_price_tiers",
                newName: "purchase_unit_id");

            // 6. Rename the referencing indexes.
            migrationBuilder.RenameIndex(
                name: "ix_purchase_order_lines_purchase_option_id",
                table: "purchase_order_lines",
                newName: "ix_purchase_order_lines_purchase_unit_id");
            migrationBuilder.RenameIndex(
                name: "ix_vendor_part_price_tiers_purchase_option_id",
                table: "vendor_part_price_tiers",
                newName: "ix_vendor_part_price_tiers_purchase_unit_id");

            // 7. Rename the referencing foreign-key constraints. The old names were
            //    truncated to 63 chars by Npgsql (trailing '~'); the new names fit.
            migrationBuilder.Sql(
                "ALTER TABLE purchase_order_lines RENAME CONSTRAINT \"fk_purchase_order_lines_part_purchase_options_purchase_option_~\" TO \"fk_purchase_order_lines_part_purchase_units_purchase_unit_id\";");
            migrationBuilder.Sql(
                "ALTER TABLE vendor_part_price_tiers RENAME CONSTRAINT \"fk_vendor_part_price_tiers_part_purchase_options_purchase_opti~\" TO \"fk_vendor_part_price_tiers_part_purchase_units_purchase_unit_id\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE vendor_part_price_tiers RENAME CONSTRAINT \"fk_vendor_part_price_tiers_part_purchase_units_purchase_unit_id\" TO \"fk_vendor_part_price_tiers_part_purchase_options_purchase_opti~\";");
            migrationBuilder.Sql(
                "ALTER TABLE purchase_order_lines RENAME CONSTRAINT \"fk_purchase_order_lines_part_purchase_units_purchase_unit_id\" TO \"fk_purchase_order_lines_part_purchase_options_purchase_option_~\";");

            migrationBuilder.RenameIndex(
                name: "ix_vendor_part_price_tiers_purchase_unit_id",
                table: "vendor_part_price_tiers",
                newName: "ix_vendor_part_price_tiers_purchase_option_id");
            migrationBuilder.RenameIndex(
                name: "ix_purchase_order_lines_purchase_unit_id",
                table: "purchase_order_lines",
                newName: "ix_purchase_order_lines_purchase_option_id");

            migrationBuilder.RenameColumn(
                name: "purchase_unit_id",
                table: "vendor_part_price_tiers",
                newName: "purchase_option_id");
            migrationBuilder.RenameColumn(
                name: "purchase_unit_id",
                table: "purchase_order_lines",
                newName: "purchase_option_id");

            migrationBuilder.Sql(
                "ALTER TABLE part_purchase_units RENAME CONSTRAINT fk_part_purchase_units_parts_part_id TO fk_part_purchase_options_parts_part_id;");
            migrationBuilder.Sql(
                "ALTER TABLE part_purchase_units RENAME CONSTRAINT \"fk_part_purchase_units__units_of_measure_content_uom_id\" TO \"fk_part_purchase_options__units_of_measure_content_uom_id\";");

            migrationBuilder.RenameIndex(
                name: "ix_part_purchase_units_part_id",
                table: "part_purchase_options",
                newName: "ix_part_purchase_options_part_id");
            migrationBuilder.RenameIndex(
                name: "ix_part_purchase_units_content_uom_id",
                table: "part_purchase_options",
                newName: "ix_part_purchase_options_content_uom_id");

            migrationBuilder.Sql(
                "ALTER TABLE part_purchase_units RENAME CONSTRAINT pk_part_purchase_units TO pk_part_purchase_options;");

            migrationBuilder.RenameTable(
                name: "part_purchase_units",
                newName: "part_purchase_options");
        }
    }
}
