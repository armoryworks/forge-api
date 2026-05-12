using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Pillar2_AddPartConceptColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "backflush_policy",
                table: "parts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_bin_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dimension_display_unit",
                table: "parts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hazmat_class",
                table: "parts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "height_mm",
                table: "parts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hts_code",
                table: "parts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_configurable",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_kit",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "length_mm",
                table: "parts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "material_spec_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "shelf_life_days",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_part_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "valuation_class_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "volume_display_unit",
                table: "parts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "volume_ml",
                table: "parts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "weight_display_unit",
                table: "parts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "weight_each",
                table: "parts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width_mm",
                table: "parts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_parts_default_bin_id",
                table: "parts",
                column: "default_bin_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_material_spec_id",
                table: "parts",
                column: "material_spec_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_source_part_id",
                table: "parts",
                column: "source_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_valuation_class_id",
                table: "parts",
                column: "valuation_class_id");

            migrationBuilder.AddForeignKey(
                name: "fk_parts__reference_data_material_spec_id",
                table: "parts",
                column: "material_spec_id",
                principalTable: "reference_data",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_parts__reference_data_valuation_class_id",
                table: "parts",
                column: "valuation_class_id",
                principalTable: "reference_data",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_parts__storage_locations_default_bin_id",
                table: "parts",
                column: "default_bin_id",
                principalTable: "storage_locations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_parts_parts_source_part_id",
                table: "parts",
                column: "source_part_id",
                principalTable: "parts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_parts__reference_data_material_spec_id",
                table: "parts");

            migrationBuilder.DropForeignKey(
                name: "fk_parts__reference_data_valuation_class_id",
                table: "parts");

            migrationBuilder.DropForeignKey(
                name: "fk_parts__storage_locations_default_bin_id",
                table: "parts");

            migrationBuilder.DropForeignKey(
                name: "fk_parts_parts_source_part_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_parts_default_bin_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_parts_material_spec_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_parts_source_part_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_parts_valuation_class_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "backflush_policy",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "default_bin_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "dimension_display_unit",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "hazmat_class",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "height_mm",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "hts_code",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "is_configurable",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "is_kit",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "length_mm",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "material_spec_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "shelf_life_days",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "source_part_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "valuation_class_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "volume_display_unit",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "volume_ml",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "weight_display_unit",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "weight_each",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "width_mm",
                table: "parts");
        }
    }
}
