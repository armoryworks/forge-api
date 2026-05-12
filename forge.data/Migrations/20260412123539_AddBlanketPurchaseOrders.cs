using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlanketPurchaseOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "agreed_unit_price",
                table: "purchase_orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "blanket_expiration_date",
                table: "purchase_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "blanket_released_quantity",
                table: "purchase_orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "blanket_total_quantity",
                table: "purchase_orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_blanket",
                table: "purchase_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "purchase_order_releases",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: false),
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    purchase_order_line_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    requested_delivery_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actual_delivery_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    receiving_record_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_releases", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_releases__receiving_records_receiving_record_id",
                        column: x => x.receiving_record_id,
                        principalTable: "receiving_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_purchase_order_releases_purchase_order_lines_purchase_order~",
                        column: x => x.purchase_order_line_id,
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_releases_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_releases_purchase_order_id_release_number",
                table: "purchase_order_releases",
                columns: new[] { "purchase_order_id", "release_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_releases_purchase_order_line_id",
                table: "purchase_order_releases",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_releases_receiving_record_id",
                table: "purchase_order_releases",
                column: "receiving_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_releases_status",
                table: "purchase_order_releases",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_releases");

            migrationBuilder.DropColumn(
                name: "agreed_unit_price",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "blanket_expiration_date",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "blanket_released_quantity",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "blanket_total_quantity",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "is_blanket",
                table: "purchase_orders");
        }
    }
}
