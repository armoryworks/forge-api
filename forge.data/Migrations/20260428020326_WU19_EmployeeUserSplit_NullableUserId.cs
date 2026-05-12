using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class WU19_EmployeeUserSplit_NullableUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employee_profiles_user_id",
                table: "employee_profiles");

            migrationBuilder.AlterColumn<int>(
                name: "user_id",
                table: "employee_profiles",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                table: "employee_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "employee_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_email",
                table: "employee_profiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_profiles_user_id",
                table: "employee_profiles",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employee_profiles_user_id",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "first_name",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "employee_profiles");

            migrationBuilder.DropColumn(
                name: "work_email",
                table: "employee_profiles");

            migrationBuilder.AlterColumn<int>(
                name: "user_id",
                table: "employee_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_profiles_user_id",
                table: "employee_profiles",
                column: "user_id",
                unique: true);
        }
    }
}
