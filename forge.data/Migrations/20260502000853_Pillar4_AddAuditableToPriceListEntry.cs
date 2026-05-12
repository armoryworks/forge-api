using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Pillar4_AddAuditableToPriceListEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Promote PriceListEntry from BaseEntity → BaseAuditableEntity so
            // entry rows carry created/updated/deleted timestamps + soft-delete
            // semantics for the new edit UI. We add the columns with NOW() as
            // a temporary default so any existing rows backfill cleanly, then
            // drop the DB-level default — handlers populate the values via the
            // standard SetTimestamps() pipeline.
            migrationBuilder.Sql(
                "ALTER TABLE price_list_entries ADD COLUMN created_at timestamp with time zone NOT NULL DEFAULT NOW();");
            migrationBuilder.Sql(
                "ALTER TABLE price_list_entries ALTER COLUMN created_at DROP DEFAULT;");

            migrationBuilder.Sql(
                "ALTER TABLE price_list_entries ADD COLUMN updated_at timestamp with time zone NOT NULL DEFAULT NOW();");
            migrationBuilder.Sql(
                "ALTER TABLE price_list_entries ALTER COLUMN updated_at DROP DEFAULT;");

            migrationBuilder.AddColumn<System.DateTimeOffset>(
                name: "deleted_at",
                table: "price_list_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deleted_by",
                table: "price_list_entries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "price_list_entries");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "price_list_entries");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                table: "price_list_entries");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "price_list_entries");
        }
    }
}
