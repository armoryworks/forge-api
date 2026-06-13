using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankStatementImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acct_bank_statement_imports",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    cash_gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    imported_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_bank_statement_imports", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_stmt_imports_cash_account",
                        column: x => x.cash_gl_account_id,
                        principalTable: "acct_gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_bank_statement_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    bank_statement_import_id = table.Column<int>(type: "integer", nullable: false),
                    cash_gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    posted_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    fitid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    match_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    matched_journal_line_id = table.Column<long>(type: "bigint", nullable: true),
                    confirmed_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_bank_statement_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_stmt_lines_import",
                        column: x => x.bank_statement_import_id,
                        principalTable: "acct_bank_statement_imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_acct_stmt_lines_journal_line",
                        column: x => x.matched_journal_line_id,
                        principalTable: "acct_journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acct_bank_statement_imports_cash_gl_account_id",
                table: "acct_bank_statement_imports",
                column: "cash_gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_stmt_imports_book_account",
                table: "acct_bank_statement_imports",
                columns: new[] { "book_id", "cash_gl_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_acct_bank_statement_lines_bank_statement_import_id",
                table: "acct_bank_statement_lines",
                column: "bank_statement_import_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_stmt_lines_journal_line",
                table: "acct_bank_statement_lines",
                column: "matched_journal_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_stmt_lines_status",
                table: "acct_bank_statement_lines",
                column: "match_status");

            migrationBuilder.CreateIndex(
                name: "ux_acct_stmt_lines_account_fitid",
                table: "acct_bank_statement_lines",
                columns: new[] { "cash_gl_account_id", "fitid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acct_bank_statement_lines");

            migrationBuilder.DropTable(
                name: "acct_bank_statement_imports");
        }
    }
}
