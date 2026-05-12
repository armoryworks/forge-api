using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase1rBatch9to16 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "account_id",
                table: "leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "assigned_to_user_id",
                table: "leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "capability_fit",
                table: "leads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "export_control",
                table: "leads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "icp_score",
                table: "leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lead_source_id",
                table: "leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "nda_expires_at",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "nda_signed_at",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nda_state",
                table: "leads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "part_class_code",
                table: "leads",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "secondary_owner_user_id",
                table: "leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_aerospace",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_automotive",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_fda_regulated",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_itar_controlled",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_reference_ok",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reference_notes",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    industry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    size_bracket = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    owner_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assignment_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    spec = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "icp_rubrics",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icp_rubrics", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lead_sources",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quality_score = table.Column<int>(type: "integer", nullable: false),
                    last_scored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sample_shipments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    lead_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    part_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cost_to_us = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    charged_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    carrier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_shipments", x => x.id);
                    table.ForeignKey(
                        name: "fk_sample_shipments_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_contacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_contacts_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "icp_dimensions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    icp_rubric_id = table.Column<int>(type: "integer", nullable: false),
                    field_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    match_spec = table.Column<string>(type: "jsonb", nullable: true),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icp_dimensions", x => x.id);
                    table.ForeignKey(
                        name: "fk_icp_dimensions__icp_rubrics_icp_rubric_id",
                        column: x => x.icp_rubric_id,
                        principalTable: "icp_rubrics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leads_account_id",
                table: "leads",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_assigned_to_user_id",
                table: "leads",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_capability_fit",
                table: "leads",
                column: "capability_fit");

            migrationBuilder.CreateIndex(
                name: "ix_leads_icp_score",
                table: "leads",
                column: "icp_score");

            migrationBuilder.CreateIndex(
                name: "ix_leads_lead_source_id",
                table: "leads",
                column: "lead_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_part_class_code",
                table: "leads",
                column: "part_class_code");

            migrationBuilder.CreateIndex(
                name: "ix_leads_secondary_owner_user_id",
                table: "leads",
                column: "secondary_owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_contacts_account_id",
                table: "account_contacts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_industry",
                table: "accounts",
                column: "industry");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_owner_user_id",
                table: "accounts",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_is_active_priority",
                table: "assignment_rules",
                columns: new[] { "is_active", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_icp_dimensions_icp_rubric_id",
                table: "icp_dimensions",
                column: "icp_rubric_id");

            migrationBuilder.CreateIndex(
                name: "ix_icp_rubrics_is_active",
                table: "icp_rubrics",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_icp_rubrics_is_default",
                table: "icp_rubrics",
                column: "is_default",
                unique: true,
                filter: "is_default = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lead_sources_code",
                table: "lead_sources",
                column: "code",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sample_shipments_lead_id",
                table: "sample_shipments",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_sample_shipments_status",
                table: "sample_shipments",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "fk_leads__lead_sources_lead_source_id",
                table: "leads",
                column: "lead_source_id",
                principalTable: "lead_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_leads_accounts_account_id",
                table: "leads",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_leads__lead_sources_lead_source_id",
                table: "leads");

            migrationBuilder.DropForeignKey(
                name: "fk_leads_accounts_account_id",
                table: "leads");

            migrationBuilder.DropTable(
                name: "account_contacts");

            migrationBuilder.DropTable(
                name: "assignment_rules");

            migrationBuilder.DropTable(
                name: "icp_dimensions");

            migrationBuilder.DropTable(
                name: "lead_sources");

            migrationBuilder.DropTable(
                name: "sample_shipments");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "icp_rubrics");

            migrationBuilder.DropIndex(
                name: "ix_leads_account_id",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_assigned_to_user_id",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_capability_fit",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_icp_score",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_lead_source_id",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_part_class_code",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_secondary_owner_user_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "assigned_to_user_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "capability_fit",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "export_control",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "icp_score",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "lead_source_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "nda_expires_at",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "nda_signed_at",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "nda_state",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "part_class_code",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "secondary_owner_user_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "is_aerospace",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "is_automotive",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "is_fda_regulated",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "is_itar_controlled",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "is_reference_ok",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "reference_notes",
                table: "customers");
        }
    }
}
