using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPartPurchaseOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "purchase_option_id",
                table: "vendor_part_price_tiers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "purchase_option_id",
                table: "purchase_order_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "part_purchase_options",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    content_uom_id = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_part_purchase_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_part_purchase_options__units_of_measure_content_uom_id",
                        column: x => x.content_uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_part_purchase_options_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_part_price_tiers_purchase_option_id",
                table: "vendor_part_price_tiers",
                column: "purchase_option_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_option_id",
                table: "purchase_order_lines",
                column: "purchase_option_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_purchase_options_content_uom_id",
                table: "part_purchase_options",
                column: "content_uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_purchase_options_part_id",
                table: "part_purchase_options",
                column: "part_id");

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_order_lines_part_purchase_options_purchase_option_~",
                table: "purchase_order_lines",
                column: "purchase_option_id",
                principalTable: "part_purchase_options",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_vendor_part_price_tiers_part_purchase_options_purchase_opti~",
                table: "vendor_part_price_tiers",
                column: "purchase_option_id",
                principalTable: "part_purchase_options",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_purchase_order_lines_part_purchase_options_purchase_option_~",
                table: "purchase_order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_vendor_part_price_tiers_part_purchase_options_purchase_opti~",
                table: "vendor_part_price_tiers");

            migrationBuilder.DropTable(
                name: "part_purchase_options");

            migrationBuilder.DropIndex(
                name: "ix_vendor_part_price_tiers_purchase_option_id",
                table: "vendor_part_price_tiers");

            migrationBuilder.DropIndex(
                name: "ix_purchase_order_lines_purchase_option_id",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "purchase_option_id",
                table: "vendor_part_price_tiers");

            migrationBuilder.DropColumn(
                name: "purchase_option_id",
                table: "purchase_order_lines");
        }
    }
}
