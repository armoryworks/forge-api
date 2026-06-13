using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorBankingNacha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_batches",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    batch_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_prenote = table.Column<bool>(type: "boolean", nullable: false),
                    effective_entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    file_contents = table.Column<string>(type: "text", nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    released_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    entry_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_bank_accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    nickname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_type = table.Column<int>(type: "integer", nullable: false),
                    routing_number_encrypted = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    account_number_encrypted = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    routing_number_masked = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_number_masked = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changed_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    approved_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    prenote_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_bank_accounts_vendor",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_batch_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payment_batch_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_payment_id = table.Column<int>(type: "integer", nullable: true),
                    vendor_bank_account_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    trace_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_batch_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_batch_items_account",
                        column: x => x.vendor_bank_account_id,
                        principalTable: "vendor_bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_batch_items_batch",
                        column: x => x.payment_batch_id,
                        principalTable: "payment_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_payment_batch_items_payment",
                        column: x => x.vendor_payment_id,
                        principalTable: "vendor_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_batch_items_account",
                table: "payment_batch_items",
                column: "vendor_bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_batch_items_payment",
                table: "payment_batch_items",
                column: "vendor_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_batch_items_payment_batch_id",
                table: "payment_batch_items",
                column: "payment_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_batches_status",
                table: "payment_batches",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_payment_batches_number",
                table: "payment_batches",
                column: "batch_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bank_accounts_status",
                table: "vendor_bank_accounts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bank_accounts_vendor",
                table: "vendor_bank_accounts",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_batch_items");

            migrationBuilder.DropTable(
                name: "vendor_bank_accounts");

            migrationBuilder.DropTable(
                name: "payment_batches");
        }
    }
}
