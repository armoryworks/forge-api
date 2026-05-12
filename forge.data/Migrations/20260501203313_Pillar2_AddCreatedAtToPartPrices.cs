using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Pillar2_AddCreatedAtToPartPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the column with NOW() as the default for the duration of this migration —
            // backfills any existing rows with a sensible (current) timestamp. The
            // application-level default in PartPriceConfiguration takes over for new rows.
            migrationBuilder.Sql(
                "ALTER TABLE part_prices ADD COLUMN created_at timestamp with time zone NOT NULL DEFAULT NOW();");

            // Drop the DB-level default — handlers set CreatedAt explicitly via IClock.
            migrationBuilder.Sql(
                "ALTER TABLE part_prices ALTER COLUMN created_at DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "part_prices");
        }
    }
}
