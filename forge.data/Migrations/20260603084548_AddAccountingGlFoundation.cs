using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingGlFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acct_books",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    functional_currency_id = table.Column<int>(type: "integer", nullable: false),
                    reporting_time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rounding_tolerance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_books", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_books_currency",
                        column: x => x.functional_currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_cost_centers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_cost_centers", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_cost_centers_book",
                        column: x => x.book_id,
                        principalTable: "acct_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_cost_centers_parent",
                        column: x => x.parent_id,
                        principalTable: "acct_cost_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_fiscal_years",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_fiscal_years", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_fiscal_years_book",
                        column: x => x.book_id,
                        principalTable: "acct_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_gl_accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    account_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normal_balance = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    parent_account_id = table.Column<int>(type: "integer", nullable: true),
                    is_control_account = table.Column<bool>(type: "boolean", nullable: false),
                    control_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_postable = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_gl_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_gl_accounts_book",
                        column: x => x.book_id,
                        principalTable: "acct_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_gl_accounts_parent",
                        column: x => x.parent_account_id,
                        principalTable: "acct_gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_fiscal_periods",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fiscal_year_id = table.Column<int>(type: "integer", nullable: false),
                    period_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_fiscal_periods", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_fiscal_periods_year",
                        column: x => x.fiscal_year_id,
                        principalTable: "acct_fiscal_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_number_sequences",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year_id = table.Column<int>(type: "integer", nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_number_sequences", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_number_sequences_book",
                        column: x => x.book_id,
                        principalTable: "acct_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_number_sequences_year",
                        column: x => x.fiscal_year_id,
                        principalTable: "acct_fiscal_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_account_determination_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: true),
                    category_id = table.Column<int>(type: "integer", nullable: true),
                    valuation_class_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_account_determination_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_determination_account",
                        column: x => x.gl_account_id,
                        principalTable: "acct_gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_determination_book",
                        column: x => x.book_id,
                        principalTable: "acct_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_journal_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    entry_number = table.Column<long>(type: "bigint", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fiscal_period_id = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year_id = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_id = table.Column<long>(type: "bigint", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    currency_id = table.Column<int>(type: "integer", nullable: false),
                    memo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    auto_reverse_next_period = table.Column<bool>(type: "boolean", nullable: false),
                    reversal_of_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversed_by_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_by = table.Column<int>(type: "integer", nullable: true),
                    posted_by = table.Column<int>(type: "integer", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_journal_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_journal_entries_book",
                        column: x => x.book_id,
                        principalTable: "acct_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_journal_entries_currency",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_journal_entries_period",
                        column: x => x.fiscal_period_id,
                        principalTable: "acct_fiscal_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_journal_entries_reversal_of",
                        column: x => x.reversal_of_entry_id,
                        principalTable: "acct_journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_journal_entries_reversed_by",
                        column: x => x.reversed_by_entry_id,
                        principalTable: "acct_journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_journal_entries_year",
                        column: x => x.fiscal_year_id,
                        principalTable: "acct_fiscal_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_ledger_balances",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    fiscal_period_id = table.Column<int>(type: "integer", nullable: false),
                    currency_id = table.Column<int>(type: "integer", nullable: false),
                    debit_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credit_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_ledger_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_ledger_balances_account",
                        column: x => x.gl_account_id,
                        principalTable: "acct_gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_ledger_balances_book",
                        column: x => x.book_id,
                        principalTable: "acct_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_ledger_balances_currency",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_ledger_balances_period",
                        column: x => x.fiscal_period_id,
                        principalTable: "acct_fiscal_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acct_journal_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    cost_center_id = table.Column<int>(type: "integer", nullable: true),
                    debit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency_id = table.Column<int>(type: "integer", nullable: false),
                    txn_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    functional_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    subledger_party_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    subledger_party_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_journal_lines", x => x.id);
                    table.CheckConstraint("ck_acct_journal_lines_debit_xor_credit", "(debit = 0) <> (credit = 0)");
                    table.ForeignKey(
                        name: "fk_acct_journal_lines_account",
                        column: x => x.gl_account_id,
                        principalTable: "acct_gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_journal_lines_cost_center",
                        column: x => x.cost_center_id,
                        principalTable: "acct_cost_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_journal_lines_currency",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_journal_lines_entry",
                        column: x => x.journal_entry_id,
                        principalTable: "acct_journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acct_journal_lines_job",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acct_account_determination_rules_gl_account_id",
                table: "acct_account_determination_rules",
                column: "gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_determination_book_key_scope",
                table: "acct_account_determination_rules",
                columns: new[] { "book_id", "key", "item_id", "category_id", "valuation_class_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_books_functional_currency_id",
                table: "acct_books",
                column: "functional_currency_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_books_code",
                table: "acct_books",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_cost_centers_parent",
                table: "acct_cost_centers",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_cost_centers_book_code",
                table: "acct_cost_centers",
                columns: new[] { "book_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_acct_fiscal_periods_year_number",
                table: "acct_fiscal_periods",
                columns: new[] { "fiscal_year_id", "period_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_acct_fiscal_years_book_name",
                table: "acct_fiscal_years",
                columns: new[] { "book_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_gl_accounts_parent",
                table: "acct_gl_accounts",
                column: "parent_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_gl_accounts_book_number",
                table: "acct_gl_accounts",
                columns: new[] { "book_id", "account_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_entries_currency_id",
                table: "acct_journal_entries",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_entries_fiscal_year_id",
                table: "acct_journal_entries",
                column: "fiscal_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_entries_period",
                table: "acct_journal_entries",
                column: "fiscal_period_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_entries_reversal_of_entry_id",
                table: "acct_journal_entries",
                column: "reversal_of_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_entries_reversed_by_entry_id",
                table: "acct_journal_entries",
                column: "reversed_by_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_entries_source",
                table: "acct_journal_entries",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ux_acct_journal_entries_book_idemp",
                table: "acct_journal_entries",
                columns: new[] { "book_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_acct_journal_entries_book_year_num",
                table: "acct_journal_entries",
                columns: new[] { "book_id", "fiscal_year_id", "entry_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_lines_account",
                table: "acct_journal_lines",
                column: "gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_lines_book",
                table: "acct_journal_lines",
                column: "book_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_lines_cost_center_id",
                table: "acct_journal_lines",
                column: "cost_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_lines_currency_id",
                table: "acct_journal_lines",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_lines_entry",
                table: "acct_journal_lines",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_lines_job_id",
                table: "acct_journal_lines",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_lines_party",
                table: "acct_journal_lines",
                columns: new[] { "subledger_party_type", "subledger_party_id" });

            migrationBuilder.CreateIndex(
                name: "ix_acct_ledger_balances_currency_id",
                table: "acct_ledger_balances",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_ledger_balances_fiscal_period_id",
                table: "acct_ledger_balances",
                column: "fiscal_period_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_ledger_balances_gl_account_id",
                table: "acct_ledger_balances",
                column: "gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_ledger_balances_grain",
                table: "acct_ledger_balances",
                columns: new[] { "book_id", "gl_account_id", "fiscal_period_id", "currency_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acct_number_sequences_fiscal_year_id",
                table: "acct_number_sequences",
                column: "fiscal_year_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_number_sequences_book_year",
                table: "acct_number_sequences",
                columns: new[] { "book_id", "fiscal_year_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acct_account_determination_rules");

            migrationBuilder.DropTable(
                name: "acct_journal_lines");

            migrationBuilder.DropTable(
                name: "acct_ledger_balances");

            migrationBuilder.DropTable(
                name: "acct_number_sequences");

            migrationBuilder.DropTable(
                name: "acct_cost_centers");

            migrationBuilder.DropTable(
                name: "acct_journal_entries");

            migrationBuilder.DropTable(
                name: "acct_gl_accounts");

            migrationBuilder.DropTable(
                name: "acct_fiscal_periods");

            migrationBuilder.DropTable(
                name: "acct_fiscal_years");

            migrationBuilder.DropTable(
                name: "acct_books");
        }
    }
}
