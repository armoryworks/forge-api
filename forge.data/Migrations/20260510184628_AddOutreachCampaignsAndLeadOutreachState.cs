using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutreachCampaignsAndLeadOutreachState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "campaign_id",
                table: "leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outreach_state",
                table: "leads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "outreach_campaigns",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    strategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    default_cooldown_days = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    owner_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outreach_campaigns", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leads_campaign_id",
                table: "leads",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_outreach_state",
                table: "leads",
                column: "outreach_state");

            migrationBuilder.CreateIndex(
                name: "ix_outreach_campaigns_is_active",
                table: "outreach_campaigns",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_outreach_campaigns_owner_user_id",
                table: "outreach_campaigns",
                column: "owner_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_leads__outreach_campaigns_campaign_id",
                table: "leads",
                column: "campaign_id",
                principalTable: "outreach_campaigns",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_leads__outreach_campaigns_campaign_id",
                table: "leads");

            migrationBuilder.DropTable(
                name: "outreach_campaigns");

            migrationBuilder.DropIndex(
                name: "ix_leads_campaign_id",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_outreach_state",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "campaign_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "outreach_state",
                table: "leads");
        }
    }
}
