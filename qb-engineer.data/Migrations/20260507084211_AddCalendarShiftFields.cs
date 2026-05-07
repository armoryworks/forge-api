using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QBEngineer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarShiftFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "capacity_hours",
                table: "shifts",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "days_of_week_mask",
                table: "shifts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "premium_multiplier",
                table: "shifts",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                // Backfill existing rows at 1.00 (standard rate). The
                // entity default also lands new inserts at 1.00.
                defaultValue: 1.00m);

            migrationBuilder.AddColumn<int>(
                name: "working_calendar_id",
                table: "shifts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_shifts_working_calendar_id",
                table: "shifts",
                column: "working_calendar_id");

            migrationBuilder.AddForeignKey(
                name: "fk_shifts__working_calendars_working_calendar_id",
                table: "shifts",
                column: "working_calendar_id",
                principalTable: "working_calendars",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_shifts__working_calendars_working_calendar_id",
                table: "shifts");

            migrationBuilder.DropIndex(
                name: "ix_shifts_working_calendar_id",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "capacity_hours",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "days_of_week_mask",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "premium_multiplier",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "working_calendar_id",
                table: "shifts");
        }
    }
}
