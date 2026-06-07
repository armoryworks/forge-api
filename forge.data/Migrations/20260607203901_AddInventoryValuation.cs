using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryValuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acct_inventory_valuations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    on_hand_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    average_unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    total_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_inventory_valuations", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_inventory_valuations_part",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acct_inventory_valuations_part_id",
                table: "acct_inventory_valuations",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_inventory_valuations_book_part",
                table: "acct_inventory_valuations",
                columns: new[] { "book_id", "part_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acct_inventory_valuations");
        }
    }
}
