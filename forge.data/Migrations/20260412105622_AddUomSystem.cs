using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUomSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "uom_id",
                table: "sales_order_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "uom_id",
                table: "purchase_order_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "purchase_uom_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sales_uom_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stock_uom_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "uom_id",
                table: "bomentries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "uom_id",
                table: "bin_contents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "units_of_measure",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decimal_places = table.Column<int>(type: "integer", nullable: false),
                    is_base_unit = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units_of_measure", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "uom_conversions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_uom_id = table.Column<int>(type: "integer", nullable: false),
                    to_uom_id = table.Column<int>(type: "integer", nullable: false),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    is_reversible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uom_conversions", x => x.id);
                    table.ForeignKey(
                        name: "fk_uom_conversions_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_uom_conversions_units_of_measure_from_uom_id",
                        column: x => x.from_uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_uom_conversions_units_of_measure_to_uom_id",
                        column: x => x.to_uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_uom_id",
                table: "sales_order_lines",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_uom_id",
                table: "purchase_order_lines",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_purchase_uom_id",
                table: "parts",
                column: "purchase_uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_sales_uom_id",
                table: "parts",
                column: "sales_uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_stock_uom_id",
                table: "parts",
                column: "stock_uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_bomentries_uom_id",
                table: "bomentries",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_contents_uom_id",
                table: "bin_contents",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_of_measure_category",
                table: "units_of_measure",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_units_of_measure_code",
                table: "units_of_measure",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_uom_conversions_from_uom_id_to_uom_id_part_id",
                table: "uom_conversions",
                columns: new[] { "from_uom_id", "to_uom_id", "part_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_uom_conversions_part_id",
                table: "uom_conversions",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_uom_conversions_to_uom_id",
                table: "uom_conversions",
                column: "to_uom_id");

            migrationBuilder.AddForeignKey(
                name: "fk_bin_contents__units_of_measure_uom_id",
                table: "bin_contents",
                column: "uom_id",
                principalTable: "units_of_measure",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_bomentries__units_of_measure_uom_id",
                table: "bomentries",
                column: "uom_id",
                principalTable: "units_of_measure",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_parts__units_of_measure_purchase_uom_id",
                table: "parts",
                column: "purchase_uom_id",
                principalTable: "units_of_measure",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_parts__units_of_measure_sales_uom_id",
                table: "parts",
                column: "sales_uom_id",
                principalTable: "units_of_measure",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_parts__units_of_measure_stock_uom_id",
                table: "parts",
                column: "stock_uom_id",
                principalTable: "units_of_measure",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_order_lines__units_of_measure_uom_id",
                table: "purchase_order_lines",
                column: "uom_id",
                principalTable: "units_of_measure",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_order_lines__units_of_measure_uom_id",
                table: "sales_order_lines",
                column: "uom_id",
                principalTable: "units_of_measure",
                principalColumn: "id");

            // Seed standard UOMs
            migrationBuilder.Sql(@"
                INSERT INTO units_of_measure (code, name, symbol, category, decimal_places, is_base_unit, is_active, sort_order) VALUES
                ('EA',  'Each',         'ea',   'Count',  0, true,  true, 1),
                ('PR',  'Pair',         'pr',   'Count',  0, false, true, 2),
                ('DZ',  'Dozen',        'dz',   'Count',  0, false, true, 3),
                ('PK',  'Pack',         'pk',   'Count',  0, false, true, 4),
                ('IN',  'Inch',         'in',   'Length', 3, false, true, 10),
                ('FT',  'Foot',         'ft',   'Length', 2, true,  true, 11),
                ('YD',  'Yard',         'yd',   'Length', 2, false, true, 12),
                ('MM',  'Millimeter',   'mm',   'Length', 1, false, true, 13),
                ('CM',  'Centimeter',   'cm',   'Length', 2, false, true, 14),
                ('M',   'Meter',        'm',    'Length', 3, false, true, 15),
                ('OZ',  'Ounce',        'oz',   'Weight', 2, false, true, 20),
                ('LB',  'Pound',        'lb',   'Weight', 2, true,  true, 21),
                ('KG',  'Kilogram',     'kg',   'Weight', 3, false, true, 22),
                ('G',   'Gram',         'g',    'Weight', 1, false, true, 23),
                ('GAL', 'Gallon',       'gal',  'Volume', 2, true,  true, 30),
                ('QT',  'Quart',        'qt',   'Volume', 2, false, true, 31),
                ('L',   'Liter',        'L',    'Volume', 3, false, true, 32),
                ('SQFT','Square Foot',  'sq ft','Area',   2, true,  true, 40),
                ('SQIN','Square Inch',  'sq in','Area',   2, false, true, 41),
                ('HR',  'Hour',         'hr',   'Time',   2, true,  true, 50),
                ('MIN', 'Minute',       'min',  'Time',   0, false, true, 51);

                -- Seed standard conversions
                INSERT INTO uom_conversions (from_uom_id, to_uom_id, conversion_factor, is_reversible) VALUES
                ((SELECT id FROM units_of_measure WHERE code='FT'), (SELECT id FROM units_of_measure WHERE code='IN'), 12, true),
                ((SELECT id FROM units_of_measure WHERE code='YD'), (SELECT id FROM units_of_measure WHERE code='FT'), 3, true),
                ((SELECT id FROM units_of_measure WHERE code='M'),  (SELECT id FROM units_of_measure WHERE code='CM'), 100, true),
                ((SELECT id FROM units_of_measure WHERE code='M'),  (SELECT id FROM units_of_measure WHERE code='MM'), 1000, true),
                ((SELECT id FROM units_of_measure WHERE code='CM'), (SELECT id FROM units_of_measure WHERE code='MM'), 10, true),
                ((SELECT id FROM units_of_measure WHERE code='FT'), (SELECT id FROM units_of_measure WHERE code='M'),  0.3048, true),
                ((SELECT id FROM units_of_measure WHERE code='IN'), (SELECT id FROM units_of_measure WHERE code='MM'), 25.4, true),
                ((SELECT id FROM units_of_measure WHERE code='LB'), (SELECT id FROM units_of_measure WHERE code='OZ'), 16, true),
                ((SELECT id FROM units_of_measure WHERE code='KG'), (SELECT id FROM units_of_measure WHERE code='LB'), 2.20462262, true),
                ((SELECT id FROM units_of_measure WHERE code='KG'), (SELECT id FROM units_of_measure WHERE code='G'),  1000, true),
                ((SELECT id FROM units_of_measure WHERE code='GAL'),(SELECT id FROM units_of_measure WHERE code='QT'), 4, true),
                ((SELECT id FROM units_of_measure WHERE code='L'),  (SELECT id FROM units_of_measure WHERE code='GAL'),0.26417205, true),
                ((SELECT id FROM units_of_measure WHERE code='DZ'), (SELECT id FROM units_of_measure WHERE code='EA'), 12, true),
                ((SELECT id FROM units_of_measure WHERE code='PR'), (SELECT id FROM units_of_measure WHERE code='EA'), 2, true),
                ((SELECT id FROM units_of_measure WHERE code='SQFT'),(SELECT id FROM units_of_measure WHERE code='SQIN'),144, true),
                ((SELECT id FROM units_of_measure WHERE code='HR'), (SELECT id FROM units_of_measure WHERE code='MIN'),60, true);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bin_contents__units_of_measure_uom_id",
                table: "bin_contents");

            migrationBuilder.DropForeignKey(
                name: "fk_bomentries__units_of_measure_uom_id",
                table: "bomentries");

            migrationBuilder.DropForeignKey(
                name: "fk_parts__units_of_measure_purchase_uom_id",
                table: "parts");

            migrationBuilder.DropForeignKey(
                name: "fk_parts__units_of_measure_sales_uom_id",
                table: "parts");

            migrationBuilder.DropForeignKey(
                name: "fk_parts__units_of_measure_stock_uom_id",
                table: "parts");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_order_lines__units_of_measure_uom_id",
                table: "purchase_order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_order_lines__units_of_measure_uom_id",
                table: "sales_order_lines");

            migrationBuilder.DropTable(
                name: "uom_conversions");

            migrationBuilder.DropTable(
                name: "units_of_measure");

            migrationBuilder.DropIndex(
                name: "ix_sales_order_lines_uom_id",
                table: "sales_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_order_lines_uom_id",
                table: "purchase_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_parts_purchase_uom_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_parts_sales_uom_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_parts_stock_uom_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_bomentries_uom_id",
                table: "bomentries");

            migrationBuilder.DropIndex(
                name: "ix_bin_contents_uom_id",
                table: "bin_contents");

            migrationBuilder.DropColumn(
                name: "uom_id",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "uom_id",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "purchase_uom_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "sales_uom_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "stock_uom_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "uom_id",
                table: "bomentries");

            migrationBuilder.DropColumn(
                name: "uom_id",
                table: "bin_contents");
        }
    }
}
