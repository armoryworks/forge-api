using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFixedAssetsDepreciation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acct_fixed_assets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    asset_tag = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    salvage_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    in_service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    useful_life_months = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    asset_gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    accumulated_depreciation_gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    depreciation_expense_gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_fixed_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "acct_depreciation_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fixed_asset_id = table.Column<int>(type: "integer", nullable: false),
                    period_month = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_depreciation_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_depreciation_entries_asset",
                        column: x => x.fixed_asset_id,
                        principalTable: "acct_fixed_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_acct_depreciation_entries_asset_month",
                table: "acct_depreciation_entries",
                columns: new[] { "fixed_asset_id", "period_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_fixed_assets_book",
                table: "acct_fixed_assets",
                column: "book_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acct_depreciation_entries");

            migrationBuilder.DropTable(
                name: "acct_fixed_assets");
        }
    }
}
