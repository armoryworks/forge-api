using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationTimeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "entry_type",
                table: "time_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "operation_id",
                table: "clock_events",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_clock_events_operation_id",
                table: "clock_events",
                column: "operation_id");

            migrationBuilder.AddForeignKey(
                name: "fk_clock_events__operations_operation_id",
                table: "clock_events",
                column: "operation_id",
                principalTable: "operations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_clock_events__operations_operation_id",
                table: "clock_events");

            migrationBuilder.DropIndex(
                name: "ix_clock_events_operation_id",
                table: "clock_events");

            migrationBuilder.DropColumn(
                name: "entry_type",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "operation_id",
                table: "clock_events");
        }
    }
}
