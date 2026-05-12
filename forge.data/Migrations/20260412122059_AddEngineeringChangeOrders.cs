using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEngineeringChangeOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "engineering_change_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    eco_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    change_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    reason_for_change = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    impact_analysis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    requested_by_id = table.Column<int>(type: "integer", nullable: false),
                    approved_by_id = table.Column<int>(type: "integer", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    implemented_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    implemented_by_id = table.Column<int>(type: "integer", nullable: true),
                    approval_request_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_engineering_change_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "eco_affected_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    eco_id = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    change_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    is_implemented = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eco_affected_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_eco_affected_items__engineering_change_orders_eco_id",
                        column: x => x.eco_id,
                        principalTable: "engineering_change_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eco_affected_items_eco_id",
                table: "eco_affected_items",
                column: "eco_id");

            migrationBuilder.CreateIndex(
                name: "ix_engineering_change_orders_approved_by_id",
                table: "engineering_change_orders",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_engineering_change_orders_eco_number",
                table: "engineering_change_orders",
                column: "eco_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_engineering_change_orders_requested_by_id",
                table: "engineering_change_orders",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_engineering_change_orders_status",
                table: "engineering_change_orders",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eco_affected_items");

            migrationBuilder.DropTable(
                name: "engineering_change_orders");
        }
    }
}
