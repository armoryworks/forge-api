using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityCapabilityRequirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_capability_requirements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capability_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirement_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    predicate = table.Column<string>(type: "jsonb", nullable: false),
                    display_name_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    missing_message_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_seed_data = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_capability_requirements", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_capability_requirements_entity_type",
                table: "entity_capability_requirements",
                column: "entity_type");

            migrationBuilder.CreateIndex(
                name: "ix_entity_capability_requirements_entity_type_capability_code_~",
                table: "entity_capability_requirements",
                columns: new[] { "entity_type", "capability_code", "requirement_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_capability_requirements");
        }
    }
}
