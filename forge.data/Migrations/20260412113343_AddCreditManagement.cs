using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "credit_hold_at",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "credit_hold_by_id",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "credit_hold_reason",
                table: "customers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "credit_limit",
                table: "customers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "credit_review_frequency_days",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_on_credit_hold",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_credit_review_date",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_credit_hold_by_id",
                table: "customers",
                column: "credit_hold_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_credit_hold_by_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "credit_hold_at",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "credit_hold_by_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "credit_hold_reason",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "credit_limit",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "credit_review_frequency_days",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "is_on_credit_hold",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "last_credit_review_date",
                table: "customers");
        }
    }
}
