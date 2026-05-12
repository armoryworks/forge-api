using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminologyPresetProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_admin_edited",
                table: "terminology_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "source_preset_id",
                table: "terminology_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_terminology_entries_source_preset_id",
                table: "terminology_entries",
                column: "source_preset_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_terminology_entries_source_preset_id",
                table: "terminology_entries");

            migrationBuilder.DropColumn(
                name: "is_admin_edited",
                table: "terminology_entries");

            migrationBuilder.DropColumn(
                name: "source_preset_id",
                table: "terminology_entries");
        }
    }
}
