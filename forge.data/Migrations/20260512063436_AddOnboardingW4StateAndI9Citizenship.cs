using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingW4StateAndI9Citizenship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "i9_alien_reg_protected",
                table: "employee_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "i9_citizenship_status",
                table: "employee_profiles",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "i9_foreign_passport_country",
                table: "employee_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "i9_foreign_passport_protected",
                table: "employee_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "i9_i94_protected",
                table: "employee_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "i9_work_auth_expiry",
                table: "employee_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "state_additional_withholding",
                table: "employee_profiles",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "state_allowances",
                table: "employee_profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "state_exempt",
                table: "employee_profiles",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state_filing_status",
                table: "employee_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "w4_deductions",
                table: "employee_profiles",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "w4_exempt_from_withholding",
                table: "employee_profiles",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "w4_extra_withholding",
                table: "employee_profiles",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "w4_filing_status",
                table: "employee_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "w4_multiple_jobs",
                table: "employee_profiles",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "w4_other_dependents",
                table: "employee_profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "w4_other_income",
                table: "employee_profiles",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "w4_qualifying_children",
                table: "employee_profiles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "i9_alien_reg_protected",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "i9_citizenship_status",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "i9_foreign_passport_country",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "i9_foreign_passport_protected",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "i9_i94_protected",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "i9_work_auth_expiry",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "state_additional_withholding",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "state_allowances",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "state_exempt",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "state_filing_status",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "w4_deductions",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "w4_exempt_from_withholding",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "w4_extra_withholding",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "w4_filing_status",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "w4_multiple_jobs",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "w4_other_dependents",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "w4_other_income",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "w4_qualifying_children",
                table: "employee_profiles");
        }
    }
}
