using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGlAccountCashFlowCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cash_flow_category",
                table: "acct_gl_accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cash_flow_category",
                table: "acct_gl_accounts");
        }
    }
}
