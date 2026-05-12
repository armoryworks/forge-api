using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_Name_To_Part : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "parts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "parts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            // Data backfill: existing rows had Description doing double duty
            // as both name and description. Copy the (possibly long) description
            // into the new short Name field, truncated to 256 chars to fit the
            // column. The Description column is preserved as-is — operators
            // can later trim it down to actual notes (or null it out) per row.
            migrationBuilder.Sql("""
                UPDATE parts
                SET name = LEFT(COALESCE(description, ''), 256)
                WHERE name = '' OR name IS NULL;
            """);

            migrationBuilder.CreateIndex(
                name: "ix_parts_name",
                table: "parts",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_parts_name",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "name",
                table: "parts");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "parts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
