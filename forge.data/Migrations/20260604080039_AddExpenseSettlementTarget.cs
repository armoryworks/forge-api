using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseSettlementTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "settlement_target",
                table: "expenses",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "vendor_id",
                table: "expenses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_expenses_vendor_id",
                table: "expenses",
                column: "vendor_id");

            migrationBuilder.AddForeignKey(
                name: "fk_expenses__vendors_vendor_id",
                table: "expenses",
                column: "vendor_id",
                principalTable: "vendors",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expenses__vendors_vendor_id",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "ix_expenses_vendor_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "settlement_target",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                table: "expenses");
        }
    }
}
