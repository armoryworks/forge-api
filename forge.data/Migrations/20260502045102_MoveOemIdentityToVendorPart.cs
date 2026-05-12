using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveOemIdentityToVendorPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_parts_manufacturer_part_number",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "external_part_number",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "manufacturer_name",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "manufacturer_part_number",
                table: "parts");

            migrationBuilder.AddColumn<string>(
                name: "manufacturer_name",
                table: "vendor_parts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_parts_vendor_mpn",
                table: "vendor_parts",
                column: "vendor_mpn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vendor_parts_vendor_mpn",
                table: "vendor_parts");

            migrationBuilder.DropColumn(
                name: "manufacturer_name",
                table: "vendor_parts");

            migrationBuilder.AddColumn<string>(
                name: "external_part_number",
                table: "parts",
                type: "character varying(100)",
                maxLength: 100,
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

            migrationBuilder.CreateIndex(
                name: "ix_parts_manufacturer_part_number",
                table: "parts",
                column: "manufacturer_part_number");
        }
    }
}
