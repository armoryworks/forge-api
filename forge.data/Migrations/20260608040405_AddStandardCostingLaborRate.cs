using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStandardCostingLaborRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "actual_labor_cost",
                table: "time_entries",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_rate_per_hour",
                table: "labor_rates",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actual_labor_cost",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "actual_rate_per_hour",
                table: "labor_rates");
        }
    }
}
