using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingPiiProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "document_name",
                table: "identity_documents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_number_protected",
                table: "identity_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issuing_authority",
                table: "identity_documents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_protected",
                table: "employee_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_type",
                table: "employee_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "employee_profiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_routing_protected",
                table: "employee_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ssn_protected",
                table: "employee_profiles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "document_name",
                table: "identity_documents");

            migrationBuilder.DropColumn(
                name: "document_number_protected",
                table: "identity_documents");

            migrationBuilder.DropColumn(
                name: "issuing_authority",
                table: "identity_documents");

            migrationBuilder.DropColumn(
                name: "bank_account_protected",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "bank_account_type",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "bank_routing_protected",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "ssn_protected",
                table: "employee_profiles");
        }
    }
}
