using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseBillPromotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "expense_id",
                table: "vendor_bills",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "job_id",
                table: "vendor_bill_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_vendor_bills_expense_live",
                table: "vendor_bills",
                column: "expense_id",
                unique: true,
                filter: "expense_id IS NOT NULL AND status <> 4");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bill_lines_job",
                table: "vendor_bill_lines",
                column: "job_id");

            migrationBuilder.AddForeignKey(
                name: "fk_vendor_bill_lines_job",
                table: "vendor_bill_lines",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_vendor_bills_expense",
                table: "vendor_bills",
                column: "expense_id",
                principalTable: "expenses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_vendor_bill_lines_job",
                table: "vendor_bill_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_vendor_bills_expense",
                table: "vendor_bills");

            migrationBuilder.DropIndex(
                name: "ux_vendor_bills_expense_live",
                table: "vendor_bills");

            migrationBuilder.DropIndex(
                name: "ix_vendor_bill_lines_job",
                table: "vendor_bill_lines");

            migrationBuilder.DropColumn(
                name: "expense_id",
                table: "vendor_bills");

            migrationBuilder.DropColumn(
                name: "job_id",
                table: "vendor_bill_lines");
        }
    }
}
