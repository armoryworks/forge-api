using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemApiKeyRoleTemplateBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "role_template_id",
                table: "system_api_keys",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_api_keys_role_template_id",
                table: "system_api_keys",
                column: "role_template_id");

            migrationBuilder.AddForeignKey(
                name: "fk_system_api_keys_role_templates_role_template_id",
                table: "system_api_keys",
                column: "role_template_id",
                principalTable: "role_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_system_api_keys_role_templates_role_template_id",
                table: "system_api_keys");

            migrationBuilder.DropIndex(
                name: "ix_system_api_keys_role_template_id",
                table: "system_api_keys");

            migrationBuilder.DropColumn(
                name: "role_template_id",
                table: "system_api_keys");
        }
    }
}
