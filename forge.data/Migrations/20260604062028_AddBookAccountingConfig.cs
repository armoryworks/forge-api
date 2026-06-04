using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAccountingConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_costing_method",
                table: "acct_books",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "revenue_recognition_method",
                table: "acct_books",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PointInTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_costing_method",
                table: "acct_books");

            migrationBuilder.DropColumn(
                name: "revenue_recognition_method",
                table: "acct_books");
        }
    }
}
