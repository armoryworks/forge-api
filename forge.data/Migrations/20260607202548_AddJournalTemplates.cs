using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acct_journal_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    auto_reverse_next_period = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_journal_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "acct_journal_template_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    journal_template_id = table.Column<int>(type: "integer", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    account_determination_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    gl_account_id = table.Column<int>(type: "integer", nullable: true),
                    debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    credit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    party_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    party_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_journal_template_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_journal_template_lines_template",
                        column: x => x.journal_template_id,
                        principalTable: "acct_journal_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acct_journal_template_lines_template",
                table: "acct_journal_template_lines",
                column: "journal_template_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_journal_templates_book_name",
                table: "acct_journal_templates",
                columns: new[] { "book_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acct_journal_template_lines");

            migrationBuilder.DropTable(
                name: "acct_journal_templates");
        }
    }
}
