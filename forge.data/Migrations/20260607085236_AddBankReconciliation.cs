using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acct_bank_reconciliations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    cash_gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    statement_date = table.Column<DateOnly>(type: "date", nullable: false),
                    statement_ending_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_bank_reconciliations", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_bank_recs_cash_account",
                        column: x => x.cash_gl_account_id,
                        principalTable: "acct_gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_bank_reconciliation_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    bank_reconciliation_id = table.Column<int>(type: "integer", nullable: false),
                    journal_line_id = table.Column<long>(type: "bigint", nullable: false),
                    is_cleared = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_bank_reconciliation_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_bank_rec_items_line",
                        column: x => x.journal_line_id,
                        principalTable: "acct_journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_bank_rec_items_rec",
                        column: x => x.bank_reconciliation_id,
                        principalTable: "acct_bank_reconciliations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acct_bank_rec_items_line",
                table: "acct_bank_reconciliation_items",
                column: "journal_line_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_bank_rec_items_rec_line",
                table: "acct_bank_reconciliation_items",
                columns: new[] { "bank_reconciliation_id", "journal_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_bank_reconciliations_cash_gl_account_id",
                table: "acct_bank_reconciliations",
                column: "cash_gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_bank_recs_book_account_date",
                table: "acct_bank_reconciliations",
                columns: new[] { "book_id", "cash_gl_account_id", "statement_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acct_bank_reconciliation_items");

            migrationBuilder.DropTable(
                name: "acct_bank_reconciliations");
        }
    }
}
