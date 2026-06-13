using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenItemSubLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acct_ap_open_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    document_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    currency_id = table.Column<int>(type: "integer", nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    original_txn_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    original_functional_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    applied_txn_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    applied_functional_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_ap_open_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_ap_open_items_book",
                        column: x => x.book_id,
                        principalTable: "acct_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_ap_open_items_currency",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_ar_open_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    document_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    currency_id = table.Column<int>(type: "integer", nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    original_txn_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    original_functional_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    applied_txn_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    applied_functional_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_ar_open_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_ar_open_items_book",
                        column: x => x.book_id,
                        principalTable: "acct_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_ar_open_items_currency",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acct_ap_open_items_book_status",
                table: "acct_ap_open_items",
                columns: new[] { "book_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_acct_ap_open_items_currency_id",
                table: "acct_ap_open_items",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_ap_open_items_vendor",
                table: "acct_ap_open_items",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_ap_open_items_source",
                table: "acct_ap_open_items",
                columns: new[] { "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_ar_open_items_book_status",
                table: "acct_ar_open_items",
                columns: new[] { "book_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_acct_ar_open_items_currency_id",
                table: "acct_ar_open_items",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_ar_open_items_customer",
                table: "acct_ar_open_items",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_ar_open_items_source",
                table: "acct_ar_open_items",
                columns: new[] { "source_type", "source_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acct_ap_open_items");

            migrationBuilder.DropTable(
                name: "acct_ar_open_items");
        }
    }
}
