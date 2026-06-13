using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCloseAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                table: "acct_fiscal_years",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "closed_by_user_id",
                table: "acct_fiscal_years",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                table: "acct_fiscal_periods",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "closed_by_user_id",
                table: "acct_fiscal_periods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reopened_at",
                table: "acct_fiscal_periods",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reopened_by_user_id",
                table: "acct_fiscal_periods",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closed_at",
                table: "acct_fiscal_years");

            migrationBuilder.DropColumn(
                name: "closed_by_user_id",
                table: "acct_fiscal_years");

            migrationBuilder.DropColumn(
                name: "closed_at",
                table: "acct_fiscal_periods");

            migrationBuilder.DropColumn(
                name: "closed_by_user_id",
                table: "acct_fiscal_periods");

            migrationBuilder.DropColumn(
                name: "reopened_at",
                table: "acct_fiscal_periods");

            migrationBuilder.DropColumn(
                name: "reopened_by_user_id",
                table: "acct_fiscal_periods");
        }
    }
}
