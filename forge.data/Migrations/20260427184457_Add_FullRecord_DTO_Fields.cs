using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_FullRecord_DTO_Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "exempt_flag",
                table: "sales_tax_rates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "gl_posting_account",
                table: "sales_tax_rates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_currency",
                table: "customers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_tax_code_id",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "acquisition_cost",
                table: "assets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "depreciation_method",
                table: "assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gl_account",
                table: "assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "work_center_id",
                table: "assets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_default_tax_code_id",
                table: "customers",
                column: "default_tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_assets_work_center_id",
                table: "assets",
                column: "work_center_id");

            migrationBuilder.AddForeignKey(
                name: "fk_assets__work_centers_work_center_id",
                table: "assets",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_customers__sales_tax_rates_default_tax_code_id",
                table: "customers",
                column: "default_tax_code_id",
                principalTable: "sales_tax_rates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assets__work_centers_work_center_id",
                table: "assets");

            migrationBuilder.DropForeignKey(
                name: "fk_customers__sales_tax_rates_default_tax_code_id",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_default_tax_code_id",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_assets_work_center_id",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "exempt_flag",
                table: "sales_tax_rates");

            migrationBuilder.DropColumn(
                name: "gl_posting_account",
                table: "sales_tax_rates");

            migrationBuilder.DropColumn(
                name: "default_currency",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "default_tax_code_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "acquisition_cost",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "depreciation_method",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "gl_account",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "work_center_id",
                table: "assets");
        }
    }
}
