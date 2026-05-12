using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeEntryBillable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "activity_type_id",
                table: "time_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "bill_rate",
                table: "time_entries",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bill_rate_currency",
                table: "time_entries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_billable",
                table: "time_entries",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_activity_type_id",
                table: "time_entries",
                column: "activity_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_time_entries_activity_type_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "activity_type_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "bill_rate",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "bill_rate_currency",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "is_billable",
                table: "time_entries");
        }
    }
}
