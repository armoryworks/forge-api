using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QBEngineer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutreachPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_outreach_preferences",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    contact_id = table.Column<int>(type: "integer", nullable: false),
                    email_opt_out = table.Column<bool>(type: "boolean", nullable: false),
                    email_opt_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_opt_out_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    call_opt_out = table.Column<bool>(type: "boolean", nullable: false),
                    call_opt_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    call_opt_out_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sms_opt_out = table.Column<bool>(type: "boolean", nullable: false),
                    sms_opt_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sms_opt_out_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cooldown_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cooldown_reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cooldown_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_outreach_preferences", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_outreach_preferences_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lead_outreach_preferences",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    lead_id = table.Column<int>(type: "integer", nullable: false),
                    email_opt_out = table.Column<bool>(type: "boolean", nullable: false),
                    email_opt_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_opt_out_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    call_opt_out = table.Column<bool>(type: "boolean", nullable: false),
                    call_opt_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    call_opt_out_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sms_opt_out = table.Column<bool>(type: "boolean", nullable: false),
                    sms_opt_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sms_opt_out_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cooldown_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cooldown_reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cooldown_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_outreach_preferences", x => x.id);
                    table.ForeignKey(
                        name: "fk_lead_outreach_preferences_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_outreach_preferences_contact_id",
                table: "contact_outreach_preferences",
                column: "contact_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contact_outreach_preferences_cooldown_until",
                table: "contact_outreach_preferences",
                column: "cooldown_until");

            migrationBuilder.CreateIndex(
                name: "ix_lead_outreach_preferences_cooldown_until",
                table: "lead_outreach_preferences",
                column: "cooldown_until");

            migrationBuilder.CreateIndex(
                name: "ix_lead_outreach_preferences_lead_id",
                table: "lead_outreach_preferences",
                column: "lead_id",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_outreach_preferences");

            migrationBuilder.DropTable(
                name: "lead_outreach_preferences");
        }
    }
}
