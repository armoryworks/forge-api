using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_RoleTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "role_template_id",
                table: "asp_net_users",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "role_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_system_default = table.Column<bool>(type: "boolean", nullable: false),
                    included_role_names_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_role_template_id",
                table: "asp_net_users",
                column: "role_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_templates_name",
                table: "role_templates",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_users_role_templates_role_template_id",
                table: "asp_net_users",
                column: "role_template_id",
                principalTable: "role_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_users_role_templates_role_template_id",
                table: "asp_net_users");

            migrationBuilder.DropTable(
                name: "role_templates");

            migrationBuilder.DropIndex(
                name: "ix_asp_net_users_role_template_id",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "role_template_id",
                table: "asp_net_users");
        }
    }
}
