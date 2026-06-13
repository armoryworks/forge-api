using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitsOfProductionDepreciation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "last_depreciated_units",
                table: "acct_fixed_assets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "linked_asset_id",
                table: "acct_fixed_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "useful_life_units",
                table: "acct_fixed_assets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_fixed_assets_linked_asset",
                table: "acct_fixed_assets",
                column: "linked_asset_id");

            migrationBuilder.AddForeignKey(
                name: "fk_acct_fixed_assets_linked_asset",
                table: "acct_fixed_assets",
                column: "linked_asset_id",
                principalTable: "assets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_acct_fixed_assets_linked_asset",
                table: "acct_fixed_assets");

            migrationBuilder.DropIndex(
                name: "ix_acct_fixed_assets_linked_asset",
                table: "acct_fixed_assets");

            migrationBuilder.DropColumn(
                name: "last_depreciated_units",
                table: "acct_fixed_assets");

            migrationBuilder.DropColumn(
                name: "linked_asset_id",
                table: "acct_fixed_assets");

            migrationBuilder.DropColumn(
                name: "useful_life_units",
                table: "acct_fixed_assets");
        }
    }
}
