using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "abc_classification_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_parts = table.Column<int>(type: "integer", nullable: false),
                    class_acount = table.Column<int>(type: "integer", nullable: false),
                    class_bcount = table.Column<int>(type: "integer", nullable: false),
                    class_ccount = table.Column<int>(type: "integer", nullable: false),
                    class_athreshold_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    class_bthreshold_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    total_annual_usage_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lookback_months = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abc_classification_runs", x => x.id);
                });

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
                name: "acct_pay_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    gross_wages = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    employee_tax_withheld = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    employer_tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_pay_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "acct_qbo_export_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    qbo_doc_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    total_debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    pushed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pushed_by = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_qbo_export_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    field_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    old_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_assistants",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    system_prompt = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    allowed_entity_types = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    starter_questions = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_built_in = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    temperature = table.Column<double>(type: "double precision", nullable: false),
                    max_context_chunks = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_assistants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "announcement_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    default_severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    default_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    default_requires_acknowledgment = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcement_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_workflows",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    activation_conditions_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_workflows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_roles", x => x.id);
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
                name: "audit_log_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    entity_id = table.Column<int>(type: "integer", nullable: true),
                    details = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bi_api_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    allowed_entity_sets_json = table.Column<string>(type: "jsonb", nullable: true),
                    allowed_ips_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bi_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "capabilities",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    area = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_default_on = table.Column<bool>(type: "boolean", nullable: false),
                    requires_roles = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capabilities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cloud_storage_providers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    provider_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    root_folder_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    service_account_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    oauth_token_encrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    refresh_token_encrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    settings = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cloud_storage_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "controlled_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    current_revision = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<int>(type: "integer", nullable: false),
                    checked_out_by_id = table.Column<int>(type: "integer", nullable: true),
                    checked_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_interval_days = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_controlled_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "costing_profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    flat_rate_pct = table.Column<decimal>(type: "numeric(7,4)", nullable: true),
                    departmental_rates = table.Column<string>(type: "jsonb", nullable: true),
                    pools = table.Column<string>(type: "jsonb", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costing_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    decimal_places = table.Column<int>(type: "integer", nullable: false),
                    is_base_currency = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_segments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    filter_criteria = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_segments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discovery_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false),
                    recommended_preset_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    applied_preset_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    recommended_confidence = table.Column<double>(type: "double precision", nullable: false),
                    applied_deltas_json = table.Column<string>(type: "jsonb", nullable: false),
                    ran_in_consultant_mode = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_embeddings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    chunk_text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    source_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    embedding = table.Column<Vector>(type: "vector(384)", nullable: true),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_embeddings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "domain_event_failures",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_payload = table.Column<string>(type: "text", nullable: false),
                    handler_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_domain_event_failures", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    work_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    date_of_birth = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    gender = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    street1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    street2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    zip_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    personal_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    emergency_contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    emergency_contact_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    emergency_contact_relationship = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    job_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    employee_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    pay_type = table.Column<int>(type: "integer", nullable: true),
                    hourly_rate = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    salary_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    w4_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state_withholding_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    i9_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    i9_expiration_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    direct_deposit_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    workers_comp_acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    handbook_acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    onboarding_bypassed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ssn_protected = table.Column<string>(type: "text", nullable: true),
                    bank_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    bank_routing_protected = table.Column<string>(type: "text", nullable: true),
                    bank_account_protected = table.Column<string>(type: "text", nullable: true),
                    bank_account_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    w4_filing_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    w4_multiple_jobs = table.Column<bool>(type: "boolean", nullable: true),
                    w4_qualifying_children = table.Column<int>(type: "integer", nullable: true),
                    w4_other_dependents = table.Column<int>(type: "integer", nullable: true),
                    w4_other_income = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    w4_deductions = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    w4_extra_withholding = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    w4_exempt_from_withholding = table.Column<bool>(type: "boolean", nullable: true),
                    state_filing_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    state_allowances = table.Column<int>(type: "integer", nullable: true),
                    state_additional_withholding = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    state_exempt = table.Column<bool>(type: "boolean", nullable: true),
                    i9_citizenship_status = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    i9_alien_reg_protected = table.Column<string>(type: "text", nullable: true),
                    i9_i94_protected = table.Column<string>(type: "text", nullable: true),
                    i9_foreign_passport_protected = table.Column<string>(type: "text", nullable: true),
                    i9_foreign_passport_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    i9_work_auth_expiry = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "engineering_change_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    eco_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    change_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    reason_for_change = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    impact_analysis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    requested_by_id = table.Column<int>(type: "integer", nullable: false),
                    approved_by_id = table.Column<int>(type: "integer", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    implemented_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    implemented_by_id = table.Column<int>(type: "integer", nullable: true),
                    approval_request_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_engineering_change_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entity_capability_requirements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capability_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirement_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    predicate = table.Column<string>(type: "jsonb", nullable: false),
                    display_name_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    missing_message_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_seed_data = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_capability_requirements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entity_readiness_validators",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    validator_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    predicate = table.Column<string>(type: "jsonb", nullable: false),
                    applicability_predicate = table.Column<string>(type: "jsonb", nullable: true),
                    display_name_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    missing_message_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_seed_data = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_readiness_validators", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    start_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    event_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    is_cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_all_day = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    is_system_generated = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
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
                name: "integration_outbox_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operation_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    entity_id = table.Column<int>(type: "integer", nullable: true),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_outbox_entries", x => x.id);
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
                name: "leave_policies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    accrual_rate_per_pay_period = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    max_balance = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    carry_over_limit = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    accrue_from_hire_date = table.Column<bool>(type: "boolean", nullable: false),
                    waiting_period_days = table.Column<int>(type: "integer", nullable: true),
                    is_paid_leave = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "master_schedules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_master_schedules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mfa_recovery_codes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_used = table.Column<bool>(type: "boolean", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_from_ip = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_recovery_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mrp_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    run_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_simulation = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    planning_horizon_days = table.Column<int>(type: "integer", nullable: false),
                    total_demand_count = table.Column<int>(type: "integer", nullable: false),
                    total_supply_count = table.Column<int>(type: "integer", nullable: false),
                    planned_order_count = table.Column<int>(type: "integer", nullable: false),
                    exception_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    initiated_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    is_dismissed = table.Column<bool>(type: "boolean", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    entity_id = table.Column<int>(type: "integer", nullable: true),
                    sender_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_state_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_state_tokens", x => x.id);
                });

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

            migrationBuilder.CreateTable(
                name: "overtime_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    daily_threshold_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    weekly_threshold_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    overtime_multiplier = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    doubletime_threshold_daily_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    doubletime_threshold_weekly_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    doubletime_multiplier = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    apply_daily_before_weekly = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overtime_rules", x => x.id);
                });

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
                name: "payment_transmissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    submission_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_transmissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pick_waves",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wave_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_to_id = table.Column<int>(type: "integer", nullable: true),
                    strategy = table.Column<int>(type: "integer", nullable: false),
                    total_lines = table.Column<int>(type: "integer", nullable: false),
                    picked_lines = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_waves", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planning_cycles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    goals = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    duration_days = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planning_cycles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recurring_expenses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    classification = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    frequency = table.Column<int>(type: "integer", nullable: false),
                    next_occurrence_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_generated_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    auto_approve = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_expenses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reference_data",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    group_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_seed_data = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reference_data", x => x.id);
                    table.ForeignKey(
                        name: "fk_reference_data_reference_data_parent_id",
                        column: x => x.parent_id,
                        principalTable: "reference_data",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "review_cycles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_cycles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_system_default = table.Column<bool>(type: "boolean", nullable: false),
                    included_role_names_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_tax_rates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    rate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    exempt_flag = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    gl_posting_account = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_tax_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schedule_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    operations_scheduled = table.Column<int>(type: "integer", nullable: false),
                    conflicts_detected = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "storage_locations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    location_type = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_locations", x => x.id);
                    table.ForeignKey(
                        name: "fk_storage_locations_storage_locations_parent_id",
                        column: x => x.parent_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supported_languages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    native_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    completion_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supported_languages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_queue_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_queue_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tariff_rates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    hts_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    country_of_origin = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    rate_pct = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariff_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terminology_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_admin_edited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    source_preset_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminology_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "track_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_shop_floor = table.Column<bool>(type: "boolean", nullable: false),
                    custom_field_definitions = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "training_paths",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    allowed_roles = table.Column<string>(type: "jsonb", nullable: true),
                    is_auto_assigned = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_paths", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "translated_labels",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    language_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    context = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_approved = table.Column<bool>(type: "boolean", nullable: false),
                    translated_by_id = table.Column<int>(type: "integer", nullable: true),
                    translated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translated_labels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "units_of_measure",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decimal_places = table.Column<int>(type: "integer", nullable: false),
                    is_base_unit = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units_of_measure", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_integrations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    encrypted_credentials = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    config_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_integrations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_mfa_devices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    device_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    encrypted_secret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    device_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    credential_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    public_key = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sign_count = table.Column<long>(type: "bigint", nullable: true),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_mfa_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    zip_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payment_terms = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deactivation_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    auto_po_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    min_order_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    off_tier_variance_pct = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_subscriptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    event_types_json = table.Column<string>(type: "text", nullable: false),
                    encrypted_secret = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    max_retries = table.Column<int>(type: "integer", nullable: false),
                    last_delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    auto_disable_on_failure = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    headers_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    default_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    steps_json = table.Column<string>(type: "jsonb", nullable: false),
                    express_template_component = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_seed_data = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: true),
                    draft_payload = table.Column<string>(type: "jsonb", nullable: true),
                    definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    current_step_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandoned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandoned_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "working_calendars",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    working_days_mask = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_calendars", x => x.id);
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

            migrationBuilder.CreateTable(
                name: "acct_pay_run_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pay_run_id = table.Column<int>(type: "integer", nullable: false),
                    employee_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    gross_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    federal_withholding = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    state_withholding = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fica_employee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    other_deductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    employer_tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_pay_run_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_pay_run_lines_run",
                        column: x => x.pay_run_id,
                        principalTable: "acct_pay_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_requests",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_id = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    current_step_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_id = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    entity_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_requests__approval_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalTable: "approval_workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "approval_steps",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_id = table.Column<int>(type: "integer", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    approver_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approver_user_id = table.Column<int>(type: "integer", nullable: true),
                    approver_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    use_direct_manager = table.Column<bool>(type: "boolean", nullable: false),
                    auto_approve_below = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    escalation_hours = table.Column<int>(type: "integer", nullable: true),
                    require_comments = table.Column<bool>(type: "boolean", nullable: false),
                    allow_delegation = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_steps__approval_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalTable: "approval_workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_role_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "capability_configs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    capability_id = table.Column<int>(type: "integer", nullable: false),
                    config_json = table.Column<string>(type: "jsonb", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capability_configs", x => x.id);
                    table.ForeignKey(
                        name: "fk_capability_configs_capabilities_capability_id",
                        column: x => x.capability_id,
                        principalTable: "capabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_cloud_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    provider_id = table.Column<int>(type: "integer", nullable: false),
                    folder_external_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    folder_path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    folder_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_via = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_cloud_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_cloud_links_cloud_storage_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "cloud_storage_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cost_calculations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    profile_id = table.Column<int>(type: "integer", nullable: false),
                    profile_version = table.Column<int>(type: "integer", nullable: false),
                    result_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    result_breakdown = table.Column<string>(type: "jsonb", nullable: true),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calculated_by = table.Column<int>(type: "integer", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_calculations", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_calculations__costing_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "costing_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    maker_checker_threshold = table.Column<decimal>(type: "numeric", nullable: true),
                    default_costing_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Standard"),
                    revenue_recognition_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PointInTime"),
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
                name: "exchange_rates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_currency_id = table.Column<int>(type: "integer", nullable: false),
                    to_currency_id = table.Column<int>(type: "integer", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_exchange_rates_currencies_from_currency_id",
                        column: x => x.from_currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exchange_rates_currencies_to_currency_id",
                        column: x => x.to_currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eco_affected_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    eco_id = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    change_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    is_implemented = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eco_affected_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_eco_affected_items__engineering_change_orders_eco_id",
                        column: x => x.eco_id,
                        principalTable: "engineering_change_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_attendees",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_attendees", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_attendees_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
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

            migrationBuilder.CreateTable(
                name: "leave_balances",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    policy_id = table.Column<int>(type: "integer", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    used_this_year = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    accrued_this_year = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    last_accrual_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_balances__leave_policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "leave_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_requests",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    policy_id = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    hours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approved_by_id = table.Column<int>(type: "integer", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    denial_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_requests_leave_policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "leave_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "performance_reviews",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cycle_id = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    reviewer_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    overall_rating = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    goals_json = table.Column<string>(type: "text", nullable: true),
                    competencies_json = table.Column<string>(type: "text", nullable: true),
                    strengths_comments = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    improvement_comments = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    employee_self_assessment = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_performance_reviews", x => x.id);
                    table.ForeignKey(
                        name: "fk_performance_reviews__review_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "review_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deactivation_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    credit_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    is_on_credit_hold = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    credit_hold_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    credit_hold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    credit_hold_by_id = table.Column<int>(type: "integer", nullable: true),
                    last_credit_review_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    credit_review_frequency_days = table.Column<int>(type: "integer", nullable: true),
                    is_tax_exempt = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    tax_exemption_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    exemption_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    default_tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    default_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_fda_regulated = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    is_aerospace = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    is_automotive = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    is_itar_controlled = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    is_reference_ok = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    reference_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.ForeignKey(
                        name: "fk_customers__sales_tax_rates_default_tax_code_id",
                        column: x => x.default_tax_code_id,
                        principalTable: "sales_tax_rates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "job_stages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    track_type_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    wiplimit = table.Column<int>(type: "integer", nullable: true),
                    accounting_document_type = table.Column<int>(type: "integer", nullable: true),
                    is_irreversible = table.Column<bool>(type: "boolean", nullable: false),
                    is_shop_floor = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_stages", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_stages__track_types_track_type_id",
                        column: x => x.track_type_id,
                        principalTable: "track_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_tasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    track_type_id = table.Column<int>(type: "integer", nullable: false),
                    internal_project_type_id = table.Column<int>(type: "integer", nullable: true),
                    assignee_id = table.Column<int>(type: "integer", nullable: true),
                    cron_expression = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_tasks__track_types_track_type_id",
                        column: x => x.track_type_id,
                        principalTable: "track_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scheduled_tasks_reference_data_internal_project_type_id",
                        column: x => x.internal_project_type_id,
                        principalTable: "reference_data",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "vendor_payments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    payment_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    payment_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_payments_vendor",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vendor_scorecards",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    total_purchase_orders = table.Column<int>(type: "integer", nullable: false),
                    total_lines_received = table.Column<int>(type: "integer", nullable: false),
                    on_time_deliveries = table.Column<int>(type: "integer", nullable: false),
                    late_deliveries = table.Column<int>(type: "integer", nullable: false),
                    early_deliveries = table.Column<int>(type: "integer", nullable: false),
                    avg_lead_time_days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    on_time_delivery_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_inspected = table.Column<int>(type: "integer", nullable: false),
                    total_accepted = table.Column<int>(type: "integer", nullable: false),
                    total_rejected = table.Column<int>(type: "integer", nullable: false),
                    total_ncrs = table.Column<int>(type: "integer", nullable: false),
                    quality_acceptance_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_spend = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    avg_price_variance_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_increase_count = table.Column<int>(type: "integer", nullable: false),
                    quantity_shortages = table.Column<int>(type: "integer", nullable: false),
                    quantity_overages = table.Column<int>(type: "integer", nullable: false),
                    quantity_accuracy_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    overall_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    grade = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calculation_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_scorecards", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_scorecards_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    duration_ms = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    is_success = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries__webhook_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "webhook_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_run_entities",
                columns: table => new
                {
                    run_id = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_run_entities", x => new { x.run_id, x.entity_type, x.entity_id });
                    table.ForeignKey(
                        name: "fk_workflow_run_entities_workflow_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_locations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    working_calendar_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_locations", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_locations__working_calendars_working_calendar_id",
                        column: x => x.working_calendar_id,
                        principalTable: "working_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    working_calendar_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    observed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_recurring = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holidays", x => x.id);
                    table.ForeignKey(
                        name: "fk_holidays__working_calendars_working_calendar_id",
                        column: x => x.working_calendar_id,
                        principalTable: "working_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    net_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    working_calendar_id = table.Column<int>(type: "integer", nullable: true),
                    days_of_week_mask = table.Column<int>(type: "integer", nullable: true),
                    premium_multiplier = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValueSql: "1.0"),
                    capacity_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false, defaultValueSql: "0.0"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_shifts__working_calendars_working_calendar_id",
                        column: x => x.working_calendar_id,
                        principalTable: "working_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_decisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    request_id = table.Column<int>(type: "integer", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    decided_by_id = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comments = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delegated_to_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_decisions__approval_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "approval_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cost_calculation_inputs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cost_calculation_id = table.Column<int>(type: "integer", nullable: false),
                    direct_material_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    direct_labor_hours = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    direct_labor_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    machine_hours = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    overhead_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    overhead_rate_pct = table.Column<decimal>(type: "numeric(7,4)", nullable: true),
                    custom_inputs = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_calculation_inputs", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_calculation_inputs_cost_calculations_cost_calculation_~",
                        column: x => x.cost_calculation_id,
                        principalTable: "cost_calculations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closed_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                    requires_job = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    requires_cost_center = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    cash_flow_category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
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
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contacts__customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_holds",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    placed_by_id = table.Column<int>(type: "integer", nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_by_id = table.Column<int>(type: "integer", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    release_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_holds", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_holds__customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_addresses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_type = table.Column<int>(type: "integer", nullable: false),
                    line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_addresses_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ecommerce_integrations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    platform = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    encrypted_credentials = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    store_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    auto_import_orders = table.Column<bool>(type: "boolean", nullable: false),
                    sync_inventory = table.Column<bool>(type: "boolean", nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    part_mappings_json = table.Column<string>(type: "jsonb", nullable: true),
                    default_customer_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ecommerce_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_ecommerce_integrations_customers_default_customer_id",
                        column: x => x.default_customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "edi_trading_partners",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    vendor_id = table.Column<int>(type: "integer", nullable: true),
                    qualifier_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    qualifier_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    interchange_sender_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    interchange_receiver_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    application_sender_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    application_receiver_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    default_format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transport_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transport_config_json = table.Column<string>(type: "jsonb", nullable: true),
                    auto_process = table.Column<bool>(type: "boolean", nullable: false),
                    require_acknowledgment = table.Column<bool>(type: "boolean", nullable: false),
                    default_mapping_profile_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    test_mode_partner_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_edi_trading_partners", x => x.id);
                    table.ForeignKey(
                        name: "fk_edi_trading_partners__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_edi_trading_partners_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    follow_up_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lost_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    converted_customer_id = table.Column<int>(type: "integer", nullable: true),
                    custom_field_values = table.Column<string>(type: "jsonb", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: false),
                    engagement_shape = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    campaign_id = table.Column<int>(type: "integer", nullable: true),
                    outreach_state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValueSql: "''"),
                    lead_source_id = table.Column<int>(type: "integer", nullable: true),
                    icp_score = table.Column<int>(type: "integer", nullable: true),
                    assigned_to_user_id = table.Column<int>(type: "integer", nullable: true),
                    account_id = table.Column<int>(type: "integer", nullable: true),
                    capability_fit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValueSql: "''"),
                    nda_state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValueSql: "''"),
                    nda_signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    nda_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    export_control = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValueSql: "''"),
                    secondary_owner_user_id = table.Column<int>(type: "integer", nullable: true),
                    part_class_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                    table.ForeignKey(
                        name: "fk_leads__lead_sources_lead_source_id",
                        column: x => x.lead_source_id,
                        principalTable: "lead_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_leads__outreach_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "outreach_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_leads_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_leads_customers_converted_customer_id",
                        column: x => x.converted_customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    payment_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    payment_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_lists",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_lists", x => x.id);
                    table.ForeignKey(
                        name: "fk_price_lists_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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

            migrationBuilder.CreateTable(
                name: "asp_net_users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    initials = table.Column<string>(type: "text", nullable: true),
                    avatar_color = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    setup_token = table.Column<string>(type: "text", nullable: true),
                    setup_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pin_hash = table.Column<string>(type: "text", nullable: true),
                    employee_barcode = table.Column<string>(type: "text", nullable: true),
                    team_id = table.Column<int>(type: "integer", nullable: true),
                    work_location_id = table.Column<int>(type: "integer", nullable: true),
                    accounting_employee_id = table.Column<string>(type: "text", nullable: true),
                    google_id = table.Column<string>(type: "text", nullable: true),
                    microsoft_id = table.Column<string>(type: "text", nullable: true),
                    oidc_subject_id = table.Column<string>(type: "text", nullable: true),
                    oidc_provider = table.Column<string>(type: "text", nullable: true),
                    mfa_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    mfa_enforced_by_policy = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    mfa_enabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mfa_recovery_codes_remaining = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    role_template_id = table.Column<int>(type: "integer", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_users_company_locations_work_location_id",
                        column: x => x.work_location_id,
                        principalTable: "company_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_asp_net_users_role_templates_role_template_id",
                        column: x => x.role_template_id,
                        principalTable: "role_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "plants",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    company_location_id = table.Column<int>(type: "integer", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plants", x => x.id);
                    table.ForeignKey(
                        name: "fk_plants_company_locations_company_location_id",
                        column: x => x.company_location_id,
                        principalTable: "company_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shift_assignments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    shift_id = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    shift_differential_rate = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_shift_assignments_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shifts",
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
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    closed_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reopened_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    reopened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                name: "acct_qbo_account_maps",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    qbo_account_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    qbo_account_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_qbo_account_maps", x => x.id);
                    table.ForeignKey(
                        name: "fk_acct_qbo_account_maps_gl_account",
                        column: x => x.gl_account_id,
                        principalTable: "acct_gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_interactions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    contact_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    interaction_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_interactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_interactions_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "customer_portal_accesses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    contact_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    one_time_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    one_time_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_portal_accesses", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_portal_accesses_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_customer_portal_accesses_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recurring_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    shipping_address_id = table.Column<int>(type: "integer", nullable: true),
                    interval_days = table.Column<int>(type: "integer", nullable: false),
                    next_generation_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_generated_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_orders_customer_addresses_shipping_address_id",
                        column: x => x.shipping_address_id,
                        principalTable: "customer_addresses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_recurring_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "edi_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trading_partner_id = table.Column<int>(type: "integer", nullable: false),
                    transaction_set = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    field_mappings_json = table.Column<string>(type: "jsonb", nullable: false),
                    value_translations_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_edi_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_edi_mappings__edi_trading_partners_trading_partner_id",
                        column: x => x.trading_partner_id,
                        principalTable: "edi_trading_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "edi_transactions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trading_partner_id = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transaction_set = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    control_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    group_control_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    transaction_control_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    raw_payload = table.Column<string>(type: "text", nullable: false),
                    parsed_data_json = table.Column<string>(type: "jsonb", nullable: true),
                    payload_size_bytes = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    related_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    related_entity_id = table.Column<int>(type: "integer", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    error_detail_json = table.Column<string>(type: "jsonb", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledgment_transaction_id = table.Column<int>(type: "integer", nullable: true),
                    is_acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_edi_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_edi_transactions_edi_trading_partners_trading_partner_id",
                        column: x => x.trading_partner_id,
                        principalTable: "edi_trading_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_edi_transactions_edi_transactions_acknowledgment_transactio~",
                        column: x => x.acknowledgment_transaction_id,
                        principalTable: "edi_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requires_acknowledgment = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_system_generated = table.Column<bool>(type: "boolean", nullable: false),
                    system_source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    template_id = table.Column<int>(type: "integer", nullable: true),
                    department_id = table.Column<int>(type: "integer", nullable: true),
                    created_by_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcements", x => x.id);
                    table.ForeignKey(
                        name: "fk_announcements__announcement_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "announcement_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_announcements__asp_net_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_user_claims__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_asp_net_user_logins__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_roles",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    role_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_tokens",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_asp_net_user_tokens__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_rooms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_group = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<int>(type: "integer", nullable: false),
                    channel_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "''"),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    team_id = table.Column<int>(type: "integer", nullable: true),
                    is_read_only = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    icon_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_by_system = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_rooms", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_rooms__asp_net_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_rooms__teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "communication_sync_configs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_connected = table.Column<bool>(type: "boolean", nullable: false),
                    access_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    refresh_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    access_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_account_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_synced_external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    config_json = table.Column<string>(type: "jsonb", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    last_error_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_communication_sync_configs", x => x.id);
                    table.ForeignKey(
                        name: "fk_communication_sync_configs__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "corrective_actions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    capa_number = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    source_entity_id = table.Column<int>(type: "integer", nullable: true),
                    source_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    problem_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    impact_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    root_cause_analysis = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    root_cause_method = table.Column<int>(type: "integer", nullable: true),
                    root_cause_method_data = table.Column<string>(type: "jsonb", nullable: true),
                    root_cause_analyzed_by_id = table.Column<int>(type: "integer", nullable: true),
                    root_cause_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    containment_action = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    corrective_action_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    preventive_action = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    verification_method = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    verification_result = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    verified_by_id = table.Column<int>(type: "integer", nullable: true),
                    verification_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effectiveness_check_due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effectiveness_check_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effectiveness_result = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    is_effective = table.Column<bool>(type: "boolean", nullable: true),
                    effectiveness_checked_by_id = table.Column<int>(type: "integer", nullable: true),
                    owner_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corrective_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_corrective_actions__asp_net_users_closed_by_id",
                        column: x => x.closed_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_corrective_actions__asp_net_users_effectiveness_checked_by_id",
                        column: x => x.effectiveness_checked_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_corrective_actions__asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_corrective_actions__asp_net_users_root_cause_analyzed_by_id",
                        column: x => x.root_cause_analyzed_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_corrective_actions__asp_net_users_verified_by_id",
                        column: x => x.verified_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cycle_counts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    location_id = table.Column<int>(type: "integer", nullable: false),
                    counted_by_id = table.Column<int>(type: "integer", nullable: false),
                    counted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_counts", x => x.id);
                    table.ForeignKey(
                        name: "fk_cycle_counts__asp_net_users_counted_by_id",
                        column: x => x.counted_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cycle_counts__storage_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entity_notes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_notes__asp_net_users_created_by",
                        column: x => x.created_by,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "follow_up_tasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    assigned_to_user_id = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_entity_id = table.Column<int>(type: "integer", nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_up_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_follow_up_tasks__asp_net_users_assigned_to_user_id",
                        column: x => x.assigned_to_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "labor_rates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    standard_rate_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    actual_rate_per_hour = table.Column<decimal>(type: "numeric", nullable: true),
                    overtime_rate_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    doubletime_rate_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labor_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_labor_rates__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    expiration_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    assigned_to_id = table.Column<int>(type: "integer", nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    estimated_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    quote_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    shipping_address_id = table.Column<int>(type: "integer", nullable: true),
                    sent_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tax_rate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    source_estimate_id = table.Column<int>(type: "integer", nullable: true),
                    converted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.id);
                    table.ForeignKey(
                        name: "fk_quotes__asp_net_users_assigned_to_id",
                        column: x => x.assigned_to_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_quotes_customer_addresses_shipping_address_id",
                        column: x => x.shipping_address_id,
                        principalTable: "customer_addresses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_quotes_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_quotes_quotes_source_estimate_id",
                        column: x => x.source_estimate_id,
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "saved_reports",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    entity_source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    columns_json = table.Column<string>(type: "jsonb", nullable: false),
                    filters_json = table.Column<string>(type: "jsonb", nullable: true),
                    group_by_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    chart_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    chart_label_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    chart_value_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_shared = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_saved_reports__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_api_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    role_template_id = table.Column<int>(type: "integer", nullable: true),
                    scopes_json = table.Column<string>(type: "jsonb", nullable: true),
                    allowed_ips_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_system_api_keys__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_system_api_keys_role_templates_role_template_id",
                        column: x => x.role_template_id,
                        principalTable: "role_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "training_modules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    content_type = table.Column<int>(type: "integer", nullable: false),
                    content_json = table.Column<string>(type: "jsonb", nullable: false),
                    cover_image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    estimated_minutes = table.Column<int>(type: "integer", nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: true),
                    app_routes = table.Column<string>(type: "jsonb", nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    is_onboarding_required = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_modules", x => x.id);
                    table.ForeignKey(
                        name: "fk_training_modules__asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "training_path_enrollments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    path_id = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_auto_assigned = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_path_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "fk_training_path_enrollments__asp_net_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_training_path_enrollments__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_training_path_enrollments_training_paths_path_id",
                        column: x => x.path_id,
                        principalTable: "training_paths",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "training_scan_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    action_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    from_location_id = table.Column<int>(type: "integer", nullable: true),
                    to_location_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    shipment_id = table.Column<int>(type: "integer", nullable: true),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    was_successful = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_scan_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_training_scan_logs__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_cloud_storage_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    provider_id = table.Column<int>(type: "integer", nullable: false),
                    external_user_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    oauth_token_encrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    refresh_token_encrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_cloud_storage_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_cloud_storage_links__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_cloud_storage_links_cloud_storage_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "cloud_storage_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_preferences",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_preferences", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_preferences__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_scan_devices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    device_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    paired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_scan_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_scan_devices__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_scan_identifiers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    identifier_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    identifier_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_scan_identifiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_scan_identifiers__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inter_plant_transfers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    from_plant_id = table.Column<int>(type: "integer", nullable: false),
                    to_plant_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_by_id = table.Column<int>(type: "integer", nullable: true),
                    received_by_id = table.Column<int>(type: "integer", nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inter_plant_transfers", x => x.id);
                    table.ForeignKey(
                        name: "fk_inter_plant_transfers__plants_from_plant_id",
                        column: x => x.from_plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inter_plant_transfers__plants_to_plant_id",
                        column: x => x.to_plant_id,
                        principalTable: "plants",
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
                name: "announcement_acknowledgments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    announcement_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcement_acknowledgments", x => x.id);
                    table.ForeignKey(
                        name: "fk_announcement_acknowledgments__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_announcement_acknowledgments_announcements_announcement_id",
                        column: x => x.announcement_id,
                        principalTable: "announcements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "announcement_teams",
                columns: table => new
                {
                    announcement_id = table.Column<int>(type: "integer", nullable: false),
                    team_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcement_teams", x => new { x.announcement_id, x.team_id });
                    table.ForeignKey(
                        name: "fk_announcement_teams__teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_announcement_teams_announcements_announcement_id",
                        column: x => x.announcement_id,
                        principalTable: "announcements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "capa_tasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    capa_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    assignee_id = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_id = table.Column<int>(type: "integer", nullable: true),
                    completion_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capa_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_capa_tasks__asp_net_users_assignee_id",
                        column: x => x.assignee_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_capa_tasks__asp_net_users_completed_by_id",
                        column: x => x.completed_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_capa_tasks__corrective_actions_capa_id",
                        column: x => x.capa_id,
                        principalTable: "corrective_actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    quote_id = table.Column<int>(type: "integer", nullable: true),
                    shipping_address_id = table.Column<int>(type: "integer", nullable: true),
                    billing_address_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    credit_terms = table.Column<int>(type: "integer", nullable: true),
                    confirmed_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_delivery_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    customer_po = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tax_rate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_orders_customer_addresses_billing_address_id",
                        column: x => x.billing_address_id,
                        principalTable: "customer_addresses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sales_orders_customer_addresses_shipping_address_id",
                        column: x => x.shipping_address_id,
                        principalTable: "customer_addresses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sales_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_orders_quotes_quote_id",
                        column: x => x.quote_id,
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "report_schedules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    saved_report_id = table.Column<int>(type: "integer", nullable: false),
                    cron_expression = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    recipient_emails_json = table.Column<string>(type: "text", nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    subject_template = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_schedules__saved_reports_saved_report_id",
                        column: x => x.saved_report_id,
                        principalTable: "saved_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "training_path_modules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    path_id = table.Column<int>(type: "integer", nullable: false),
                    module_id = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_path_modules", x => x.id);
                    table.ForeignKey(
                        name: "fk_training_path_modules_training_modules_module_id",
                        column: x => x.module_id,
                        principalTable: "training_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_training_path_modules_training_paths_path_id",
                        column: x => x.path_id,
                        principalTable: "training_paths",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "training_progress",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    module_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    quiz_score = table.Column<int>(type: "integer", nullable: true),
                    quiz_attempts = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    time_spent_seconds = table.Column<int>(type: "integer", nullable: false),
                    quiz_answers_json = table.Column<string>(type: "jsonb", nullable: true),
                    quiz_session_json = table.Column<string>(type: "text", nullable: true),
                    walkthrough_step_reached = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_training_progress__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_training_progress_training_modules_module_id",
                        column: x => x.module_id,
                        principalTable: "training_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ecommerce_order_syncs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    integration_id = table.Column<int>(type: "integer", nullable: false),
                    external_order_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_order_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sales_order_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    order_data_json = table.Column<string>(type: "jsonb", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ecommerce_order_syncs", x => x.id);
                    table.ForeignKey(
                        name: "fk_ecommerce_order_syncs__sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ecommerce_order_syncs_ecommerce_integrations_integration_id",
                        column: x => x.integration_id,
                        principalTable: "ecommerce_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    sales_order_id = table.Column<int>(type: "integer", nullable: true),
                    budget_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    committed_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimate_at_completion_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    planned_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    revenue_recognized = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    percent_complete = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects__sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_projects_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    shipment_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sales_order_id = table.Column<int>(type: "integer", nullable: false),
                    shipping_address_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    shipped_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipping_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    weight = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    service_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    estimated_delivery_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    freight_class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    insured_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    signature_required = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    bill_of_lading_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipments_customer_addresses_shipping_address_id",
                        column: x => x.shipping_address_id,
                        principalTable: "customer_addresses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_shipments_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wbs_elements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<int>(type: "integer", nullable: false),
                    parent_element_id = table.Column<int>(type: "integer", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    budget_labor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    budget_material = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    budget_other = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    budget_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_labor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_material = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_other = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    percent_complete = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wbs_elements", x => x.id);
                    table.ForeignKey(
                        name: "fk_wbs_elements_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wbs_elements_wbs_elements_parent_element_id",
                        column: x => x.parent_element_id,
                        principalTable: "wbs_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    invoice_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    currency_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    fx_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, defaultValue: 1m),
                    sales_order_id = table.Column<int>(type: "integer", nullable: true),
                    shipment_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    invoice_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    credit_terms = table.Column<int>(type: "integer", nullable: true),
                    tax_rate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    customer_po = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoices__sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_invoices__shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_invoices_currencies_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_packages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    shipment_id = table.Column<int>(type: "integer", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    weight = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    length = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    width = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    height = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_packages", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_packages_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wbs_cost_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wbs_element_id = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    source_entity_id = table.Column<int>(type: "integer", nullable: true),
                    entry_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wbs_cost_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_wbs_cost_entries__wbs_elements_wbs_element_id",
                        column: x => x.wbs_element_id,
                        principalTable: "wbs_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_applications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payment_id = table.Column<int>(type: "integer", nullable: false),
                    invoice_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    settlement_fx_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, defaultValue: 1m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_applications_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_payment_applications_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "abc_classifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    classification = table.Column<int>(type: "integer", nullable: false),
                    annual_usage_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    annual_demand_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cumulative_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    run_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abc_classifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_abc_classifications__abc_classification_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "abc_classification_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                        name: "fk_acct_bank_rec_items_rec",
                        column: x => x.bank_reconciliation_id,
                        principalTable: "acct_bank_reconciliations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                });

            migrationBuilder.CreateTable(
                name: "acct_depreciation_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fixed_asset_id = table.Column<int>(type: "integer", nullable: false),
                    period_month = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_depreciation_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "acct_fixed_assets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    asset_tag = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    salvage_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    in_service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    useful_life_months = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    useful_life_units = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    linked_asset_id = table.Column<int>(type: "integer", nullable: true),
                    last_depreciated_units = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    asset_gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    accumulated_depreciation_gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    depreciation_expense_gl_account_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acct_fixed_assets", x => x.id);
                });

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
                });

            migrationBuilder.CreateTable(
                name: "andon_alerts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_id = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by_id = table.Column<int>(type: "integer", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    job_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_andon_alerts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    asset_type = table.Column<int>(type: "integer", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    manufacturer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    photo_file_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    current_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_customer_owned = table.Column<bool>(type: "boolean", nullable: false),
                    cavity_count = table.Column<int>(type: "integer", nullable: true),
                    tool_life_expectancy = table.Column<int>(type: "integer", nullable: true),
                    current_shot_count = table.Column<int>(type: "integer", nullable: false),
                    source_job_id = table.Column<int>(type: "integer", nullable: true),
                    source_part_id = table.Column<int>(type: "integer", nullable: true),
                    acquisition_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    depreciation_method = table.Column<int>(type: "integer", nullable: true),
                    work_center_id = table.Column<int>(type: "integer", nullable: true),
                    gl_account = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    gage_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    gage_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    manufacturer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    calibration_interval_days = table.Column<int>(type: "integer", nullable: false),
                    last_calibrated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_calibration_due = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    location_id = table.Column<int>(type: "integer", nullable: true),
                    asset_id = table.Column<int>(type: "integer", nullable: true),
                    accuracy_spec = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    range_spec = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resolution = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gages", x => x.id);
                    table.ForeignKey(
                        name: "fk_gages__storage_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_gages_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "work_centers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    company_location_id = table.Column<int>(type: "integer", nullable: true),
                    asset_id = table.Column<int>(type: "integer", nullable: true),
                    daily_capacity_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    efficiency_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    number_of_machines = table.Column<int>(type: "integer", nullable: false),
                    labor_cost_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    burden_rate_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ideal_cycle_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_centers", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_centers_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_work_centers_company_locations_company_location_id",
                        column: x => x.company_location_id,
                        principalTable: "company_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "kiosk_terminals",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    device_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    team_id = table.Column<int>(type: "integer", nullable: false),
                    configured_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kiosk_terminals", x => x.id);
                    table.ForeignKey(
                        name: "fk_kiosk_terminals__teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_kiosk_terminals__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "machine_connections",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    opc_ua_endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    security_policy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    auth_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    encrypted_credentials = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    poll_interval_ms = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_connections__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ml_models",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    model_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    model_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    trained_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    training_sample_count = table.Column<int>(type: "integer", nullable: false),
                    accuracy = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    precision = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    recall = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    f1_score = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    hyperparameters_json = table.Column<string>(type: "jsonb", nullable: true),
                    feature_list_json = table.Column<string>(type: "jsonb", nullable: true),
                    model_artifact_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    work_center_id = table.Column<int>(type: "integer", nullable: true),
                    prediction_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ml_models", x => x.id);
                    table.ForeignKey(
                        name: "fk_ml_models__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "work_center_calendars",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    available_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_center_calendars", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_center_calendars_work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_center_qualifications",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    qualified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    qualified_by_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_center_qualifications", x => new { x.user_id, x.work_center_id });
                    table.ForeignKey(
                        name: "fk_work_center_qualifications__asp_net_users_qualified_by_id",
                        column: x => x.qualified_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_work_center_qualifications__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_work_center_qualifications_work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_center_shifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    shift_id = table.Column<int>(type: "integer", nullable: false),
                    days_of_week = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_center_shifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_center_shifts_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_work_center_shifts_work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "machine_tags",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    connection_id = table.Column<int>(type: "integer", nullable: false),
                    tag_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    opc_node_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    warning_threshold_low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    warning_threshold_high = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    alarm_threshold_low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    alarm_threshold_high = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_tags_machine_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "machine_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "machine_data_points",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tag_id = table.Column<int>(type: "integer", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_data_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_data_points__machine_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "machine_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auto_po_suggestions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    suggested_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    needed_by_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_sales_order_ids = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    converted_purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_po_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_auto_po_suggestions__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "barcodes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    sales_order_id = table.Column<int>(type: "integer", nullable: true),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    asset_id = table.Column<int>(type: "integer", nullable: true),
                    storage_location_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barcodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_barcodes__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_barcodes__sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_barcodes__storage_locations_storage_location_id",
                        column: x => x.storage_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_barcodes_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bin_contents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    location_id = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    placed_by = table.Column<int>(type: "integer", nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    uom_id = table.Column<int>(type: "integer", nullable: true),
                    reserved_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bin_contents", x => x.id);
                    table.ForeignKey(
                        name: "fk_bin_contents__storage_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bin_contents__units_of_measure_uom_id",
                        column: x => x.uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "cycle_count_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cycle_count_id = table.Column<int>(type: "integer", nullable: false),
                    bin_content_id = table.Column<int>(type: "integer", nullable: true),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    expected_quantity = table.Column<int>(type: "integer", nullable: false),
                    actual_quantity = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_count_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_cycle_count_lines_bin_contents_bin_content_id",
                        column: x => x.bin_content_id,
                        principalTable: "bin_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cycle_count_lines_cycle_counts_cycle_count_id",
                        column: x => x.cycle_count_id,
                        principalTable: "cycle_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bin_movements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    from_location_id = table.Column<int>(type: "integer", nullable: true),
                    to_location_id = table.Column<int>(type: "integer", nullable: true),
                    moved_by = table.Column<int>(type: "integer", nullable: false),
                    moved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reversed_movement_id = table.Column<int>(type: "integer", nullable: true),
                    scan_action_log_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bin_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_bin_movements__storage_locations_from_location_id",
                        column: x => x.from_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bin_movements__storage_locations_to_location_id",
                        column: x => x.to_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bin_movements_bin_movements_reversed_movement_id",
                        column: x => x.reversed_movement_id,
                        principalTable: "bin_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bom_revision_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    bom_revision_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: true),
                    reference_designator = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bom_revision_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bom_revisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    effective_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bom_revisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, defaultValueSql: "''"),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    revision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    procurement_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValueSql: "'Buy'"),
                    inventory_class = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValueSql: "'Component'"),
                    item_kind_id = table.Column<int>(type: "integer", nullable: true),
                    traceability_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValueSql: "'None'"),
                    abc_class = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    material_spec_id = table.Column<int>(type: "integer", nullable: true),
                    weight_each = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    weight_display_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    length_mm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    width_mm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    height_mm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    dimension_display_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    volume_ml = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    volume_display_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    valuation_class_id = table.Column<int>(type: "integer", nullable: true),
                    hts_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    hazmat_class = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    shelf_life_days = table.Column<int>(type: "integer", nullable: true),
                    backflush_policy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    is_kit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_configurable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    default_bin_id = table.Column<int>(type: "integer", nullable: true),
                    source_part_id = table.Column<int>(type: "integer", nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    preferred_vendor_id = table.Column<int>(type: "integer", nullable: true),
                    safety_stock_qty = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    exclude_from_auto_po = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    min_stock_threshold = table.Column<decimal>(type: "numeric", nullable: true),
                    reorder_point = table.Column<decimal>(type: "numeric", nullable: true),
                    reorder_quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    safety_stock_days = table.Column<int>(type: "integer", nullable: true),
                    lot_sizing_rule = table.Column<int>(type: "integer", nullable: true),
                    fixed_order_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    minimum_order_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    order_multiple = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    planning_fence_days = table.Column<int>(type: "integer", nullable: true),
                    demand_fence_days = table.Column<int>(type: "integer", nullable: true),
                    is_mrp_planned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    requires_receiving_inspection = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    receiving_inspection_template_id = table.Column<int>(type: "integer", nullable: true),
                    inspection_frequency = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    inspection_skip_after_n = table.Column<int>(type: "integer", nullable: true),
                    custom_field_values = table.Column<string>(type: "jsonb", nullable: true),
                    stock_uom_id = table.Column<int>(type: "integer", nullable: true),
                    purchase_uom_id = table.Column<int>(type: "integer", nullable: true),
                    sales_uom_id = table.Column<int>(type: "integer", nullable: true),
                    tooling_asset_id = table.Column<int>(type: "integer", nullable: true),
                    manual_cost_override = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    current_cost_calculation_id = table.Column<int>(type: "integer", nullable: true),
                    current_bom_revision_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_parts__reference_data_item_kind_id",
                        column: x => x.item_kind_id,
                        principalTable: "reference_data",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_parts__reference_data_material_spec_id",
                        column: x => x.material_spec_id,
                        principalTable: "reference_data",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_parts__reference_data_valuation_class_id",
                        column: x => x.valuation_class_id,
                        principalTable: "reference_data",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_parts__storage_locations_default_bin_id",
                        column: x => x.default_bin_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_parts__units_of_measure_purchase_uom_id",
                        column: x => x.purchase_uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_parts__units_of_measure_sales_uom_id",
                        column: x => x.sales_uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_parts__units_of_measure_stock_uom_id",
                        column: x => x.stock_uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_parts__vendors_preferred_vendor_id",
                        column: x => x.preferred_vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_parts_assets_tooling_asset_id",
                        column: x => x.tooling_asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_parts_bom_revisions_current_bom_revision_id",
                        column: x => x.current_bom_revision_id,
                        principalTable: "bom_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_parts_cost_calculations_current_cost_calculation_id",
                        column: x => x.current_cost_calculation_id,
                        principalTable: "cost_calculations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_parts_parts_source_part_id",
                        column: x => x.source_part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bomlines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    parent_part_id = table.Column<int>(type: "integer", nullable: false),
                    child_part_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reference_designator = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    uom_id = table.Column<int>(type: "integer", nullable: true),
                    vendor_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bomlines", x => x.id);
                    table.ForeignKey(
                        name: "fk_bomlines__parts_child_part_id",
                        column: x => x.child_part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bomlines__parts_parent_part_id",
                        column: x => x.parent_part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bomlines__units_of_measure_uom_id",
                        column: x => x.uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_bomlines__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "consignment_agreements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    agreed_unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    min_stock_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    max_stock_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    invoice_on_consumption = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reconciliation_frequency_days = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consignment_agreements", x => x.id);
                    table.ForeignKey(
                        name: "fk_consignment_agreements__customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_consignment_agreements__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consignment_agreements__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "demand_forecasts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    historical_periods = table.Column<int>(type: "integer", nullable: false),
                    forecast_periods = table.Column<int>(type: "integer", nullable: false),
                    smoothing_factor = table.Column<double>(type: "double precision", nullable: true),
                    forecast_start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    forecast_data_json = table.Column<string>(type: "jsonb", nullable: true),
                    applied_to_master_schedule_id = table.Column<int>(type: "integer", nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demand_forecasts", x => x.id);
                    table.ForeignKey(
                        name: "fk_demand_forecasts__master_schedules_applied_to_master_schedule~",
                        column: x => x.applied_to_master_schedule_id,
                        principalTable: "master_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_demand_forecasts__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inter_plant_transfer_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    from_location_id = table.Column<int>(type: "integer", nullable: true),
                    to_location_id = table.Column<int>(type: "integer", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inter_plant_transfer_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_inter_plant_transfer_lines__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inter_plant_transfer_lines_inter_plant_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalTable: "inter_plant_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    invoice_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    tax_code = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_lines__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "kanban_cards",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    storage_location_id = table.Column<int>(type: "integer", nullable: true),
                    bin_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    number_of_bins = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    supply_source = table.Column<int>(type: "integer", nullable: false),
                    supply_vendor_id = table.Column<int>(type: "integer", nullable: true),
                    supply_work_center_id = table.Column<int>(type: "integer", nullable: true),
                    lead_time_days = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    last_triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_replenished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active_order_id = table.Column<int>(type: "integer", nullable: true),
                    active_order_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    trigger_count = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kanban_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_kanban_cards__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_kanban_cards__storage_locations_storage_location_id",
                        column: x => x.storage_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_kanban_cards__vendors_supply_vendor_id",
                        column: x => x.supply_vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_kanban_cards__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "master_schedule_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    master_schedule_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_master_schedule_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_master_schedule_lines__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_master_schedule_lines_master_schedules_master_schedule_id",
                        column: x => x.master_schedule_id,
                        principalTable: "master_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mrp_exceptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mrp_run_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    exception_type = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    suggested_action = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_exceptions__mrp_runs_mrp_run_id",
                        column: x => x.mrp_run_id,
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mrp_exceptions__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_supplies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mrp_run_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_entity_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    available_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    allocated_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_supplies", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_supplies__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mrp_supplies_mrp_runs_mrp_run_id",
                        column: x => x.mrp_run_id,
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    work_center_id = table.Column<int>(type: "integer", nullable: true),
                    asset_id = table.Column<int>(type: "integer", nullable: true),
                    estimated_minutes = table.Column<int>(type: "integer", nullable: true),
                    is_qc_checkpoint = table.Column<bool>(type: "boolean", nullable: false),
                    qc_criteria = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    referenced_operation_id = table.Column<int>(type: "integer", nullable: true),
                    setup_minutes = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValueSql: "0.0"),
                    run_minutes_each = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValueSql: "0.0"),
                    run_minutes_lot = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValueSql: "0.0"),
                    overlap_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValueSql: "0.0"),
                    scrap_factor = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    is_subcontract = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    subcontract_vendor_id = table.Column<int>(type: "integer", nullable: true),
                    subcontract_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    subcontract_lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    subcontract_instructions = table.Column<string>(type: "text", nullable: true),
                    subcontract_turn_time_days = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    labor_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    burden_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    estimated_labor_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    estimated_burden_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_operations__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_operations__vendors_subcontract_vendor_id",
                        column: x => x.subcontract_vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_operations__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_operations_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_operations_operations_referenced_operation_id",
                        column: x => x.referenced_operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "part_alternates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    alternate_part_id = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    is_approved = table.Column<bool>(type: "boolean", nullable: false),
                    approved_by_id = table.Column<int>(type: "integer", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_bidirectional = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_part_alternates", x => x.id);
                    table.ForeignKey(
                        name: "fk_part_alternates_parts_alternate_part_id",
                        column: x => x.alternate_part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_part_alternates_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "part_prices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_part_prices", x => x.id);
                    table.ForeignKey(
                        name: "fk_part_prices_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "part_purchase_units",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    content_uom_id = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_part_purchase_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_part_purchase_units__units_of_measure_content_uom_id",
                        column: x => x.content_uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_part_purchase_units_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "part_revisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    change_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    change_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    effective_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_part_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_part_revisions_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ppap_submissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    submission_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    ppap_level = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    part_revision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    customer_contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    customer_response_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    internal_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    psw_signed_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    psw_signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ppap_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_ppap_submissions__asp_net_users_psw_signed_by_user_id",
                        column: x => x.psw_signed_by_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ppap_submissions_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ppap_submissions_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_list_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    price_list_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    min_quantity = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_list_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_price_list_entries_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_price_list_entries_price_lists_price_list_id",
                        column: x => x.price_list_id,
                        principalTable: "price_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_configurators",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    base_part_id = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    validation_rules_json = table.Column<string>(type: "text", nullable: true),
                    base_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    pricing_formula_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_configurators", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_configurators_parts_base_part_id",
                        column: x => x.base_part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "qc_checklist_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qc_checklist_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_qc_checklist_templates_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quote_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    quote_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_lines_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_quote_lines_quotes_quote_id",
                        column: x => x.quote_id,
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recurring_order_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    recurring_order_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_order_lines_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_order_lines_recurring_orders_recurring_order_id",
                        column: x => x.recurring_order_id,
                        principalTable: "recurring_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "request_for_quotes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rfq_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    required_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    special_instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    response_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    awarded_vendor_response_id = table.Column<int>(type: "integer", nullable: true),
                    generated_purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_for_quotes", x => x.id);
                    table.ForeignKey(
                        name: "fk_request_for_quotes_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sales_order_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    shipped_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    uom_id = table.Column<int>(type: "integer", nullable: true),
                    tax_code = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_order_lines__units_of_measure_uom_id",
                        column: x => x.uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_order_lines_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sales_order_lines_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_action_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    action_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    part_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    from_location_id = table.Column<int>(type: "integer", nullable: true),
                    to_location_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    related_entity_id = table.Column<int>(type: "integer", nullable: true),
                    related_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reversed_by_log_id = table.Column<int>(type: "integer", nullable: true),
                    reverses_log_id = table.Column<int>(type: "integer", nullable: true),
                    is_reversed = table.Column<bool>(type: "boolean", nullable: false),
                    is_training_mode = table.Column<bool>(type: "boolean", nullable: false),
                    kiosk_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    device_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_action_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_scan_action_logs__storage_locations_from_location_id",
                        column: x => x.from_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scan_action_logs__storage_locations_to_location_id",
                        column: x => x.to_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scan_action_logs_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "uom_conversions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_uom_id = table.Column<int>(type: "integer", nullable: false),
                    to_uom_id = table.Column<int>(type: "integer", nullable: false),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    is_reversible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uom_conversions", x => x.id);
                    table.ForeignKey(
                        name: "fk_uom_conversions_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_uom_conversions_units_of_measure_from_uom_id",
                        column: x => x.from_uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_uom_conversions_units_of_measure_to_uom_id",
                        column: x => x.to_uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vendor_parts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_part_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    manufacturer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vendor_mpn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    min_order_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    pack_size = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    country_of_origin = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    hts_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_manufacturer = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_approved = table.Column<bool>(type: "boolean", nullable: false),
                    is_preferred = table.Column<bool>(type: "boolean", nullable: false),
                    certifications = table.Column<string>(type: "jsonb", nullable: true),
                    last_quoted_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    incoterm = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_parts_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vendor_parts_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "forecast_overrides",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    demand_forecast_id = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    override_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    overridden_by_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forecast_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_forecast_overrides_demand_forecasts_demand_forecast_id",
                        column: x => x.demand_forecast_id,
                        principalTable: "demand_forecasts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "kanban_trigger_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kanban_card_id = table.Column<int>(type: "integer", nullable: false),
                    trigger_type = table.Column<int>(type: "integer", nullable: false),
                    triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fulfilled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fulfilled_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    order_id = table.Column<int>(type: "integer", nullable: true),
                    order_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    triggered_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kanban_trigger_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_kanban_trigger_logs_kanban_cards_kanban_card_id",
                        column: x => x.kanban_card_id,
                        principalTable: "kanban_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clock_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    event_type_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scan_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clock_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_clock_events__operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "operation_materials",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    operation_id = table.Column<int>(type: "integer", nullable: false),
                    bom_line_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operation_materials", x => x.id);
                    table.ForeignKey(
                        name: "fk_operation_materials_bomlines_bom_line_id",
                        column: x => x.bom_line_id,
                        principalTable: "bomlines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_operation_materials_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "spc_characteristics",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    measurement_type = table.Column<int>(type: "integer", nullable: false),
                    nominal_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    upper_spec_limit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    lower_spec_limit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    decimal_places = table.Column<int>(type: "integer", nullable: false),
                    sample_size = table.Column<int>(type: "integer", nullable: false),
                    sample_frequency = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    gage_id = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notify_on_ooc = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spc_characteristics", x => x.id);
                    table.ForeignKey(
                        name: "fk_spc_characteristics_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_spc_characteristics_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "status_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    status_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    set_by_id = table.Column<int>(type: "integer", nullable: true),
                    work_center_id = table.Column<int>(type: "integer", nullable: true),
                    operation_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_status_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_status_entries__asp_net_users_set_by_id",
                        column: x => x.set_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_status_entries__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_status_entries_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_attachments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    bucket_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    uploaded_by_id = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<string>(type: "text", nullable: true),
                    expiration_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    part_revision_id = table.Column<int>(type: "integer", nullable: true),
                    required_role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sensitivity = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_attachments__part_revisions_part_revision_id",
                        column: x => x.part_revision_id,
                        principalTable: "part_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "fmea_analyses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fmea_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    operation_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    prepared_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    responsibility = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    original_date = table.Column<DateOnly>(type: "date", nullable: true),
                    revision_date = table.Column<DateOnly>(type: "date", nullable: true),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ppap_submission_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fmea_analyses", x => x.id);
                    table.ForeignKey(
                        name: "fk_fmea_analyses__operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_fmea_analyses__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_fmea_analyses__ppap_submissions_ppap_submission_id",
                        column: x => x.ppap_submission_id,
                        principalTable: "ppap_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ppap_elements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    submission_id = table.Column<int>(type: "integer", nullable: false),
                    element_number = table.Column<int>(type: "integer", nullable: false),
                    element_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    assigned_to_user_id = table.Column<int>(type: "integer", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ppap_elements", x => x.id);
                    table.ForeignKey(
                        name: "fk_ppap_elements__asp_net_users_assigned_to_user_id",
                        column: x => x.assigned_to_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ppap_elements__ppap_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "ppap_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "configurator_options",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    configurator_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    option_type = table.Column<int>(type: "integer", nullable: false),
                    values_json = table.Column<string>(type: "text", nullable: false),
                    pricing_rule_json = table.Column<string>(type: "text", nullable: true),
                    bom_impact_json = table.Column<string>(type: "text", nullable: true),
                    routing_impact_json = table.Column<string>(type: "text", nullable: true),
                    depends_on_option_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    help_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    default_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configurator_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_configurator_options__product_configurators_configurator_id",
                        column: x => x.configurator_id,
                        principalTable: "product_configurators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_configurations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    configurator_id = table.Column<int>(type: "integer", nullable: false),
                    configuration_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    selections_json = table.Column<string>(type: "text", nullable: false),
                    computed_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    generated_bom_json = table.Column<string>(type: "text", nullable: true),
                    generated_routing_json = table.Column<string>(type: "text", nullable: true),
                    quote_id = table.Column<int>(type: "integer", nullable: true),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_configurations", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_configurations__product_configurators_configurator_id",
                        column: x => x.configurator_id,
                        principalTable: "product_configurators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_configurations__quotes_quote_id",
                        column: x => x.quote_id,
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_product_configurations_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "qc_checklist_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    specification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qc_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_qc_checklist_items__qc_checklist_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "qc_checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_vendor_responses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rfq_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    minimum_order_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    tooling_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    quote_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_awarded = table.Column<bool>(type: "boolean", nullable: false),
                    decline_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_vendor_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfq_vendor_responses__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rfq_vendor_responses_request_for_quotes_rfq_id",
                        column: x => x.rfq_id,
                        principalTable: "request_for_quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipment_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    shipment_id = table.Column<int>(type: "integer", nullable: false),
                    sales_order_line_id = table.Column<int>(type: "integer", nullable: true),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    weight = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    length = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    width = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    height = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    is_hazmat = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    handling_instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    serial_numbers = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    inventory_relieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_lines_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_lines_sales_order_lines_sales_order_line_id",
                        column: x => x.sales_order_line_id,
                        principalTable: "sales_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_lines_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendor_part_price_tiers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_part_id = table.Column<int>(type: "integer", nullable: false),
                    purchase_unit_id = table.Column<int>(type: "integer", nullable: true),
                    min_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    freight_included = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_part_price_tiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_part_price_tiers_part_purchase_units_purchase_unit_id",
                        column: x => x.purchase_unit_id,
                        principalTable: "part_purchase_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vendor_part_price_tiers_vendor_parts_vendor_part_id",
                        column: x => x.vendor_part_id,
                        principalTable: "vendor_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "spc_control_limits",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    characteristic_id = table.Column<int>(type: "integer", nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sample_count = table.Column<int>(type: "integer", nullable: false),
                    from_subgroup = table.Column<int>(type: "integer", nullable: false),
                    to_subgroup = table.Column<int>(type: "integer", nullable: false),
                    xbar_ucl = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    xbar_lcl = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    xbar_center_line = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    range_ucl = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    range_lcl = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    range_center_line = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    sucl = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    slcl = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    scenter_line = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    cp = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    cpk = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    pp = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    ppk = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    process_sigma = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spc_control_limits", x => x.id);
                    table.ForeignKey(
                        name: "fk_spc_control_limits_spc_characteristics_characteristic_id",
                        column: x => x.characteristic_id,
                        principalTable: "spc_characteristics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calibration_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    gage_id = table.Column<int>(type: "integer", nullable: false),
                    calibrated_by_id = table.Column<int>(type: "integer", nullable: false),
                    calibrated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    result = table.Column<int>(type: "integer", nullable: false),
                    lab_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    certificate_file_id = table.Column<int>(type: "integer", nullable: true),
                    standards_used = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    as_found_condition = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    as_left_condition = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    next_calibration_due = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calibration_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_calibration_records__file_attachments_certificate_file_id",
                        column: x => x.certificate_file_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_calibration_records__gages_gage_id",
                        column: x => x.gage_id,
                        principalTable: "gages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sender_id = table.Column<int>(type: "integer", nullable: false),
                    recipient_id = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    chat_room_id = table.Column<int>(type: "integer", nullable: true),
                    file_attachment_id = table.Column<int>(type: "integer", nullable: true),
                    linked_entity_type = table.Column<string>(type: "text", nullable: true),
                    linked_entity_id = table.Column<int>(type: "integer", nullable: true),
                    parent_message_id = table.Column<int>(type: "integer", nullable: true),
                    thread_reply_count = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    thread_last_reply_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages__asp_net_users_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_messages__asp_net_users_sender_id",
                        column: x => x.sender_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_messages__chat_rooms_chat_room_id",
                        column: x => x.chat_room_id,
                        principalTable: "chat_rooms",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_chat_messages__file_attachments_file_attachment_id",
                        column: x => x.file_attachment_id,
                        principalTable: "file_attachments",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_messages_parent_message_id",
                        column: x => x.parent_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "compliance_form_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    form_type = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sha256_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_auto_sync = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    requires_identity_docs = table.Column<bool>(type: "boolean", nullable: false),
                    docu_seal_template_id = table.Column<int>(type: "integer", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    manual_override_file_id = table.Column<int>(type: "integer", nullable: true),
                    blocks_job_assignment = table.Column<bool>(type: "boolean", nullable: false),
                    profile_completion_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    acro_field_map_json = table.Column<string>(type: "jsonb", nullable: true),
                    filled_pdf_template_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compliance_form_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_compliance_form_templates__file_attachments_filled_pdf_templa~",
                        column: x => x.filled_pdf_template_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_compliance_form_templates__file_attachments_manual_override_f~",
                        column: x => x.manual_override_file_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "document_revisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    file_attachment_id = table.Column<int>(type: "integer", nullable: false),
                    change_description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    authored_by_id = table.Column<int>(type: "integer", nullable: false),
                    reviewed_by_id = table.Column<int>(type: "integer", nullable: true),
                    approved_by_id = table.Column<int>(type: "integer", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_revisions__file_attachments_file_attachment_id",
                        column: x => x.file_attachment_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_revisions_controlled_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "controlled_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    file_attachment_id = table.Column<int>(type: "integer", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_by_id = table.Column<int>(type: "integer", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    document_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    issuing_authority = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    document_number_protected = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_documents_file_attachments_file_attachment_id",
                        column: x => x.file_attachment_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pay_stubs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    pay_period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pay_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pay_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gross_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_deductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_taxes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    file_attachment_id = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_stubs", x => x.id);
                    table.ForeignKey(
                        name: "fk_pay_stubs_file_attachments_file_attachment_id",
                        column: x => x.file_attachment_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tax_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    employer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    file_attachment_id = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_documents_file_attachments_file_attachment_id",
                        column: x => x.file_attachment_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "fmea_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fmea_id = table.Column<int>(type: "integer", nullable: false),
                    item_number = table.Column<int>(type: "integer", nullable: false),
                    process_step = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    function = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    failure_mode = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    potential_effect = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    classification = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    potential_cause = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurrence = table.Column<int>(type: "integer", nullable: false),
                    current_prevention_controls = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    current_detection_controls = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    detection = table.Column<int>(type: "integer", nullable: false),
                    recommended_action = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    responsible_user_id = table.Column<int>(type: "integer", nullable: true),
                    target_completion_date = table.Column<DateOnly>(type: "date", nullable: true),
                    action_taken = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    action_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revised_severity = table.Column<int>(type: "integer", nullable: true),
                    revised_occurrence = table.Column<int>(type: "integer", nullable: true),
                    revised_detection = table.Column<int>(type: "integer", nullable: true),
                    capa_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fmea_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_fmea_items__asp_net_users_responsible_user_id",
                        column: x => x.responsible_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fmea_items_corrective_actions_capa_id",
                        column: x => x.capa_id,
                        principalTable: "corrective_actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_fmea_items_fmea_analyses_fmea_id",
                        column: x => x.fmea_id,
                        principalTable: "fmea_analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pick_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wave_id = table.Column<int>(type: "integer", nullable: false),
                    shipment_line_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    from_location_id = table.Column<int>(type: "integer", nullable: false),
                    from_bin_id = table.Column<int>(type: "integer", nullable: true),
                    bin_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    picked_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    picked_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    picked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    short_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_pick_lines__pick_waves_wave_id",
                        column: x => x.wave_id,
                        principalTable: "pick_waves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pick_lines__shipment_lines_shipment_line_id",
                        column: x => x.shipment_line_id,
                        principalTable: "shipment_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_lines__storage_locations_from_location_id",
                        column: x => x.from_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_lines_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_message_mentions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_message_id = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    display_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_message_mentions", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_message_mentions_chat_messages_chat_message_id",
                        column: x => x.chat_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_room_members",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_room_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "''"),
                    muted_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_read_message_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_room_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_room_members__asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_room_members_chat_messages_last_read_message_id",
                        column: x => x.last_read_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_chat_room_members_chat_rooms_chat_room_id",
                        column: x => x.chat_room_id,
                        principalTable: "chat_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_definition_versions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_id = table.Column<int>(type: "integer", nullable: true),
                    state_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    form_definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    source_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sha256_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    effective_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expiration_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    field_count = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    visual_comparison_json = table.Column<string>(type: "jsonb", nullable: true),
                    visual_similarity_score = table.Column<double>(type: "double precision", nullable: true),
                    visual_comparison_passed = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_definition_versions", x => x.id);
                    table.CheckConstraint("ck_form_definition_versions_scope", "template_id IS NOT NULL OR state_code IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_form_definition_versions_compliance_form_templates_template~",
                        column: x => x.template_id,
                        principalTable: "compliance_form_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pay_stub_deductions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pay_stub_id = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_stub_deductions", x => x.id);
                    table.ForeignKey(
                        name: "fk_pay_stub_deductions_pay_stubs_pay_stub_id",
                        column: x => x.pay_stub_id,
                        principalTable: "pay_stubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "compliance_form_submissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    docu_seal_submission_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    signed_pdf_file_id = table.Column<int>(type: "integer", nullable: true),
                    docu_seal_submit_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    form_data_json = table.Column<string>(type: "jsonb", nullable: true),
                    form_definition_version_id = table.Column<int>(type: "integer", nullable: true),
                    filled_pdf_file_id = table.Column<int>(type: "integer", nullable: true),
                    i9_section1_signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    i9_section2_signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    i9_employer_user_id = table.Column<int>(type: "integer", nullable: true),
                    i9_document_list_type = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    i9_document_data_json = table.Column<string>(type: "jsonb", nullable: true),
                    i9_section2_overdue_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    i9_reverification_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compliance_form_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_compliance_form_submissions__compliance_form_templates_templat~",
                        column: x => x.template_id,
                        principalTable: "compliance_form_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_compliance_form_submissions__file_attachments_filled_pdf_file~",
                        column: x => x.filled_pdf_file_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_compliance_form_submissions__file_attachments_signed_pdf_file~",
                        column: x => x.signed_pdf_file_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_compliance_form_submissions__form_definition_versions_form_def~",
                        column: x => x.form_definition_version_id,
                        principalTable: "form_definition_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "consignment_transactions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    agreement_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    extended_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    invoice_id = table.Column<int>(type: "integer", nullable: true),
                    bin_content_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consignment_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_consignment_transactions__invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_consignment_transactions_consignment_agreements_agreement_id",
                        column: x => x.agreement_id,
                        principalTable: "consignment_agreements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_returns",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    return_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    original_job_id = table.Column<int>(type: "integer", nullable: false),
                    rework_job_id = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    return_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    inspected_by_id = table.Column<int>(type: "integer", nullable: true),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inspection_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_returns", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_returns_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deliverables",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    project_id = table.Column<int>(type: "integer", nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    deliverable_type_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Draft"),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    file_attachment_ids = table.Column<string>(type: "jsonb", nullable: true),
                    cloud_link_external_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deliverables", x => x.id);
                    table.ForeignKey(
                        name: "fk_deliverables__projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_deliverables_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "downtime_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    asset_id = table.Column<int>(type: "integer", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: true),
                    reported_by_id = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    downtime_reason_id = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_planned = table.Column<bool>(type: "boolean", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_downtime_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_downtime_logs__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_downtime_logs_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    receipt_file_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approved_by = table.Column<int>(type: "integer", nullable: true),
                    approval_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    external_expense_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_id = table.Column<string>(type: "text", nullable: true),
                    external_ref = table.Column<string>(type: "text", nullable: true),
                    provider = table.Column<string>(type: "text", nullable: true),
                    expense_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settlement_target = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    vendor_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expenses", x => x.id);
                    table.ForeignKey(
                        name: "fk_expenses__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "job_activity_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    action = table.Column<int>(type: "integer", nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    old_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: true),
                    operation_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_activity_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_activity_logs__operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_activity_logs__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_job_id = table.Column<int>(type: "integer", nullable: false),
                    target_job_id = table.Column<int>(type: "integer", nullable: false),
                    link_type = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_notes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_notes__asp_net_users_created_by",
                        column: x => x.created_by,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "job_parts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    job_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_parts__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_subtasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    assignee_id = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_subtasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    job_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    track_type_id = table.Column<int>(type: "integer", nullable: false),
                    current_stage_id = table.Column<int>(type: "integer", nullable: false),
                    assignee_id = table.Column<int>(type: "integer", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    board_position = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    parent_job_id = table.Column<int>(type: "integer", nullable: true),
                    sales_order_line_id = table.Column<int>(type: "integer", nullable: true),
                    mrp_planned_order_id = table.Column<int>(type: "integer", nullable: true),
                    bom_revision_id_at_release = table.Column<int>(type: "integer", nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    iteration_count = table.Column<int>(type: "integer", nullable: false),
                    iteration_notes = table.Column<string>(type: "text", nullable: true),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    internal_project_type_id = table.Column<int>(type: "integer", nullable: true),
                    estimated_material_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    estimated_labor_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    estimated_burden_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    estimated_subcontract_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    quoted_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    disposition = table.Column<int>(type: "integer", nullable: true),
                    disposition_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    disposition_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    custom_field_values = table.Column<string>(type: "jsonb", nullable: true),
                    engagement_type_id = table.Column<int>(type: "integer", nullable: true),
                    project_phase_id = table.Column<int>(type: "integer", nullable: true),
                    billing_model = table.Column<int>(type: "integer", nullable: true),
                    retainer_hours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    retainer_balance_hours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    sow_id = table.Column<int>(type: "integer", nullable: true),
                    cover_photo_file_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_jobs__job_stages_current_stage_id",
                        column: x => x.current_stage_id,
                        principalTable: "job_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs__sales_order_lines_sales_order_line_id",
                        column: x => x.sales_order_line_id,
                        principalTable: "sales_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_jobs__track_types_track_type_id",
                        column: x => x.track_type_id,
                        principalTable: "track_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_bom_revisions_bom_revision_id_at_release",
                        column: x => x.bom_revision_id_at_release,
                        principalTable: "bom_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_jobs_file_attachments_cover_photo_file_id",
                        column: x => x.cover_photo_file_id,
                        principalTable: "file_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_jobs_jobs_parent_job_id",
                        column: x => x.parent_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_predictions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    prediction_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    confidence_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    predicted_failure_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    remaining_useful_life_hours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    model_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    input_features_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    predicted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    preventive_maintenance_job_id = table.Column<int>(type: "integer", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    was_accurate = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_predictions", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_predictions__asp_net_users_acknowledged_by_user_id",
                        column: x => x.acknowledged_by_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_predictions__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_predictions_jobs_preventive_maintenance_job_id",
                        column: x => x.preventive_maintenance_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_schedules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    asset_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    interval_days = table.Column<int>(type: "integer", nullable: false),
                    interval_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    last_performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    maintenance_job_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_schedules_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_schedules_jobs_maintenance_job_id",
                        column: x => x.maintenance_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "material_issues",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    issued_by_id = table.Column<int>(type: "integer", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bin_content_id = table.Column<int>(type: "integer", nullable: true),
                    storage_location_id = table.Column<int>(type: "integer", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issue_type = table.Column<int>(type: "integer", nullable: false),
                    return_reason_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_issues", x => x.id);
                    table.ForeignKey(
                        name: "fk_material_issues__asp_net_users_issued_by_id",
                        column: x => x.issued_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_material_issues__operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_material_issues__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_material_issues__storage_locations_storage_location_id",
                        column: x => x.storage_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_material_issues_bin_contents_bin_content_id",
                        column: x => x.bin_content_id,
                        principalTable: "bin_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_material_issues_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "non_conformances",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ncr_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    production_run_id = table.Column<int>(type: "integer", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sales_order_line_id = table.Column<int>(type: "integer", nullable: true),
                    purchase_order_line_id = table.Column<int>(type: "integer", nullable: true),
                    qc_inspection_id = table.Column<int>(type: "integer", nullable: true),
                    detected_by_id = table.Column<int>(type: "integer", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detected_at_stage = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    affected_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    defective_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    containment_actions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    containment_by_id = table.Column<int>(type: "integer", nullable: true),
                    containment_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disposition_code = table.Column<int>(type: "integer", nullable: true),
                    disposition_by_id = table.Column<int>(type: "integer", nullable: true),
                    disposition_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disposition_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    rework_instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    material_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    labor_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    total_cost_impact = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    capa_id = table.Column<int>(type: "integer", nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    vendor_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_non_conformances", x => x.id);
                    table.ForeignKey(
                        name: "fk_non_conformances__asp_net_users_containment_by_id",
                        column: x => x.containment_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_non_conformances__asp_net_users_detected_by_id",
                        column: x => x.detected_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_non_conformances__asp_net_users_disposition_by_id",
                        column: x => x.disposition_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_non_conformances__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_non_conformances__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_non_conformances_corrective_actions_capa_id",
                        column: x => x.capa_id,
                        principalTable: "corrective_actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_non_conformances_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_non_conformances_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "planning_cycle_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planning_cycle_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_rolled_over = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planning_cycle_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_planning_cycle_entries_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_planning_cycle_entries_planning_cycles_planning_cycle_id",
                        column: x => x.planning_cycle_id,
                        principalTable: "planning_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    operator_id = table.Column<int>(type: "integer", nullable: true),
                    work_center_id = table.Column<int>(type: "integer", nullable: true),
                    run_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_quantity = table.Column<int>(type: "integer", nullable: false),
                    completed_quantity = table.Column<int>(type: "integer", nullable: false),
                    scrap_quantity = table.Column<int>(type: "integer", nullable: false),
                    rework_quantity = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_to_stock_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_quantity = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    setup_time_minutes = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    run_time_minutes = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    ideal_cycle_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    actual_cycle_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_runs__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_production_runs_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_production_runs_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    ponumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    submitted_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expected_delivery_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    short_close_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    short_closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_blanket = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    blanket_total_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    blanket_released_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    blanket_expiration_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    agreed_unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    incoterm = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    estimated_freight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    quote_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    fx_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    fx_rate_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_orders__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    bin_content_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    sales_order_line_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservations", x => x.id);
                    table.ForeignKey(
                        name: "fk_reservations__sales_order_lines_sales_order_line_id",
                        column: x => x.sales_order_line_id,
                        principalTable: "sales_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reservations_bin_contents_bin_content_id",
                        column: x => x.bin_content_id,
                        principalTable: "bin_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reservations_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reservations_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "schedule_milestones",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sales_order_line_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    milestone_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actual_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_schedule_milestones_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_schedule_milestones_sales_order_lines_sales_order_line_id",
                        column: x => x.sales_order_line_id,
                        principalTable: "sales_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_operations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: false),
                    work_center_id = table.Column<int>(type: "integer", nullable: false),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    setup_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    setup_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    setup_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    run_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    total_hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    schedule_run_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_operations__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scheduled_operations_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scheduled_operations_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scheduled_operations_schedule_runs_schedule_run_id",
                        column: x => x.schedule_run_id,
                        principalTable: "schedule_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "serial_numbers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    serial_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    lot_record_id = table.Column<int>(type: "integer", nullable: true),
                    current_location_id = table.Column<int>(type: "integer", nullable: true),
                    shipment_line_id = table.Column<int>(type: "integer", nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    parent_serial_id = table.Column<int>(type: "integer", nullable: true),
                    manufactured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scrapped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serial_numbers", x => x.id);
                    table.ForeignKey(
                        name: "fk_serial_numbers__storage_locations_current_location_id",
                        column: x => x.current_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_serial_numbers_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_serial_numbers_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_numbers_serial_numbers_parent_serial_id",
                        column: x => x.parent_serial_id,
                        principalTable: "serial_numbers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "spc_measurements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    characteristic_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    production_run_id = table.Column<int>(type: "integer", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    measured_by_id = table.Column<int>(type: "integer", nullable: false),
                    measured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    subgroup_number = table.Column<int>(type: "integer", nullable: false),
                    values_json = table.Column<string>(type: "jsonb", nullable: false),
                    mean = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    range = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    std_dev = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    median = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    is_out_of_spec = table.Column<bool>(type: "boolean", nullable: false),
                    is_out_of_control = table.Column<bool>(type: "boolean", nullable: false),
                    ooc_rule_violated = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spc_measurements", x => x.id);
                    table.ForeignKey(
                        name: "fk_spc_measurements__asp_net_users_measured_by_id",
                        column: x => x.measured_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_spc_measurements_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_spc_measurements_spc_characteristics_characteristic_id",
                        column: x => x.characteristic_id,
                        principalTable: "spc_characteristics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "time_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    timer_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    timer_stop = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_manual = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    accounting_time_activity_id = table.Column<string>(type: "text", nullable: true),
                    operation_id = table.Column<int>(type: "integer", nullable: true),
                    entry_type = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    work_center_id = table.Column<int>(type: "integer", nullable: true),
                    labor_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    actual_labor_cost = table.Column<decimal>(type: "numeric", nullable: false, defaultValueSql: "0.0"),
                    burden_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValueSql: "0.0"),
                    is_billable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    bill_rate = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    bill_rate_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    activity_type_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_time_entries__work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_time_entries_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_time_entries_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "prediction_feedbacks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    prediction_id = table.Column<int>(type: "integer", nullable: false),
                    actual_failure_occurred = table.Column<bool>(type: "boolean", nullable: false),
                    actual_failure_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    prediction_error_hours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_by_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prediction_feedbacks", x => x.id);
                    table.ForeignKey(
                        name: "fk_prediction_feedbacks__asp_net_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prediction_feedbacks_maintenance_predictions_prediction_id",
                        column: x => x.prediction_id,
                        principalTable: "maintenance_predictions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    maintenance_schedule_id = table.Column<int>(type: "integer", nullable: false),
                    performed_by_id = table.Column<int>(type: "integer", nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    hours_at_service = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_logs__maintenance_schedules_maintenance_schedule_~",
                        column: x => x.maintenance_schedule_id,
                        principalTable: "maintenance_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qc_inspections",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    production_run_id = table.Column<int>(type: "integer", nullable: true),
                    template_id = table.Column<int>(type: "integer", nullable: true),
                    inspector_id = table.Column<int>(type: "integer", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qc_inspections", x => x.id);
                    table.ForeignKey(
                        name: "fk_qc_inspections_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_qc_inspections_production_runs_production_run_id",
                        column: x => x.production_run_id,
                        principalTable: "production_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_qc_inspections_qc_checklist_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "qc_checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_planned_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mrp_run_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    order_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_firmed = table.Column<bool>(type: "boolean", nullable: false),
                    released_purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    released_job_id = table.Column<int>(type: "integer", nullable: true),
                    parent_planned_order_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_planned_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders__mrp_runs_mrp_run_id",
                        column: x => x.mrp_run_id,
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders__purchase_orders_released_purchase_order_id",
                        column: x => x.released_purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders_jobs_released_job_id",
                        column: x => x.released_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders_mrp_planned_orders_parent_planned_order_~",
                        column: x => x.parent_planned_order_id,
                        principalTable: "mrp_planned_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "reorder_suggestions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: true),
                    current_stock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    available_stock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    burn_rate_daily_avg = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    burn_rate_window_days = table.Column<int>(type: "integer", nullable: false),
                    days_of_stock_remaining = table.Column<int>(type: "integer", nullable: true),
                    projected_stockout_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    incoming_po_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    earliest_po_arrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suggested_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approved_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resulting_purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    dismissed_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dismiss_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reorder_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_reorder_suggestions__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reorder_suggestions_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reorder_suggestions_purchase_orders_resulting_purchase_orde~",
                        column: x => x.resulting_purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "subcontract_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<int>(type: "integer", nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_return_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_by_id = table.Column<int>(type: "integer", nullable: true),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    shipping_tracking_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    return_tracking_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ncr_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subcontract_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_subcontract_orders__vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subcontract_orders_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subcontract_orders_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subcontract_orders_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "vendor_bills",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    bill_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    currency_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    fx_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, defaultValue: 1m),
                    vendor_invoice_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: true),
                    expense_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    bill_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    credit_terms = table.Column<int>(type: "integer", nullable: true),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_bills", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_bills_currency",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vendor_bills_expense",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vendor_bills_po",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_vendor_bills_vendor",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "serial_histories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    serial_number_id = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    from_location_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    to_location_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    actor_id = table.Column<int>(type: "integer", nullable: true),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serial_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_serial_histories__serial_numbers_serial_number_id",
                        column: x => x.serial_number_id,
                        principalTable: "serial_numbers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "spc_ooc_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    characteristic_id = table.Column<int>(type: "integer", nullable: false),
                    measurement_id = table.Column<int>(type: "integer", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rule_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    acknowledged_by_id = table.Column<int>(type: "integer", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledgment_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    capa_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spc_ooc_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_spc_ooc_events__asp_net_users_acknowledged_by_id",
                        column: x => x.acknowledged_by_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_spc_ooc_events_spc_characteristics_characteristic_id",
                        column: x => x.characteristic_id,
                        principalTable: "spc_characteristics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_spc_ooc_events_spc_measurements_measurement_id",
                        column: x => x.measurement_id,
                        principalTable: "spc_measurements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "time_correction_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    time_entry_id = table.Column<int>(type: "integer", nullable: false),
                    corrected_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_job_id = table.Column<int>(type: "integer", nullable: true),
                    original_date = table.Column<DateOnly>(type: "date", nullable: false),
                    original_duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    original_start_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    original_end_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    original_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    original_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_correction_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_time_correction_logs__time_entries_time_entry_id",
                        column: x => x.time_entry_id,
                        principalTable: "time_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qc_inspection_results",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    inspection_id = table.Column<int>(type: "integer", nullable: false),
                    checklist_item_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    passed = table.Column<bool>(type: "boolean", nullable: false),
                    measured_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qc_inspection_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_qc_inspection_results_qc_checklist_items_checklist_item_id",
                        column: x => x.checklist_item_id,
                        principalTable: "qc_checklist_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_qc_inspection_results_qc_inspections_inspection_id",
                        column: x => x.inspection_id,
                        principalTable: "qc_inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mrp_demands",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mrp_run_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_entity_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    required_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_dependent = table.Column<bool>(type: "boolean", nullable: false),
                    parent_planned_order_id = table.Column<int>(type: "integer", nullable: true),
                    bom_level = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_demands", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_demands__mrp_planned_orders_parent_planned_order_id",
                        column: x => x.parent_planned_order_id,
                        principalTable: "mrp_planned_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mrp_demands__mrp_runs_mrp_run_id",
                        column: x => x.mrp_run_id,
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mrp_demands__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ordered_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cancelled_short_close_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    billed_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    mrp_planned_order_id = table.Column<int>(type: "integer", nullable: true),
                    uom_id = table.Column<int>(type: "integer", nullable: true),
                    purchase_unit_id = table.Column<int>(type: "integer", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines__units_of_measure_uom_id",
                        column: x => x.uom_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_mrp_planned_orders_mrp_planned_order_id",
                        column: x => x.mrp_planned_order_id,
                        principalTable: "mrp_planned_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_part_purchase_units_purchase_unit_id",
                        column: x => x.purchase_unit_id,
                        principalTable: "part_purchase_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendor_payment_applications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_payment_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_bill_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    settlement_fx_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, defaultValue: 1m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_payment_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_vpa_bill",
                        column: x => x.vendor_bill_id,
                        principalTable: "vendor_bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vpa_payment",
                        column: x => x.vendor_payment_id,
                        principalTable: "vendor_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lot_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    production_run_id = table.Column<int>(type: "integer", nullable: true),
                    purchase_order_line_id = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    expiration_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    supplier_lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lot_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_lot_records__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lot_records__production_runs_production_run_id",
                        column: x => x.production_run_id,
                        principalTable: "production_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lot_records__purchase_order_lines_purchase_order_line_id",
                        column: x => x.purchase_order_line_id,
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lot_records_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receiving_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchase_order_line_id = table.Column<int>(type: "integer", nullable: false),
                    quantity_received = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    storage_location_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    inspection_status = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    inspected_by_id = table.Column<int>(type: "integer", nullable: true),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inspection_notes = table.Column<string>(type: "text", nullable: true),
                    inspected_quantity_accepted = table.Column<decimal>(type: "numeric", nullable: true),
                    inspected_quantity_rejected = table.Column<decimal>(type: "numeric", nullable: true),
                    qc_inspection_id = table.Column<int>(type: "integer", nullable: true),
                    receipt_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    actual_freight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    freight_allocation_method = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    allocated_freight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receiving_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_receiving_records__storage_locations_storage_location_id",
                        column: x => x.storage_location_id,
                        principalTable: "storage_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receiving_records_purchase_order_lines_purchase_order_line_~",
                        column: x => x.purchase_order_line_id,
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendor_bill_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_bill_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    purchase_order_line_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<int>(type: "integer", nullable: true),
                    account_determination_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_bill_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_bill_lines_bill",
                        column: x => x.vendor_bill_id,
                        principalTable: "vendor_bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vendor_bill_lines_job",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_vendor_bill_lines_part",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_vendor_bill_lines_po_line",
                        column: x => x.purchase_order_line_id,
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_releases",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: false),
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    purchase_order_line_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    requested_delivery_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actual_delivery_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    receiving_record_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_releases", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_releases__receiving_records_receiving_record_id",
                        column: x => x.receiving_record_id,
                        principalTable: "receiving_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_purchase_order_releases_purchase_order_lines_purchase_order~",
                        column: x => x.purchase_order_line_id,
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_releases_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receiving_inspections",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    receiving_record_id = table.Column<int>(type: "integer", nullable: false),
                    qc_inspection_id = table.Column<int>(type: "integer", nullable: true),
                    result = table.Column<int>(type: "integer", nullable: false),
                    accepted_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    rejected_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    inspected_by_id = table.Column<int>(type: "integer", nullable: false),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ncr_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receiving_inspections", x => x.id);
                    table.ForeignKey(
                        name: "fk_receiving_inspections__receiving_records_receiving_record_id",
                        column: x => x.receiving_record_id,
                        principalTable: "receiving_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_receiving_inspections_qc_inspections_qc_inspection_id",
                        column: x => x.qc_inspection_id,
                        principalTable: "qc_inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_abc_classifications_classification",
                table: "abc_classifications",
                column: "classification");

            migrationBuilder.CreateIndex(
                name: "ix_abc_classifications_part_id",
                table: "abc_classifications",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_abc_classifications_run_id",
                table: "abc_classifications",
                column: "run_id");

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
                name: "ix_acct_account_determination_rules_gl_account_id",
                table: "acct_account_determination_rules",
                column: "gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_determination_book_key_scope",
                table: "acct_account_determination_rules",
                columns: new[] { "book_id", "key", "item_id", "category_id", "valuation_class_id" },
                unique: true);

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
                name: "ux_acct_depreciation_entries_asset_month",
                table: "acct_depreciation_entries",
                columns: new[] { "fixed_asset_id", "period_month" },
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
                name: "ix_acct_fixed_assets_book",
                table: "acct_fixed_assets",
                column: "book_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_fixed_assets_linked_asset",
                table: "acct_fixed_assets",
                column: "linked_asset_id");

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
                name: "ix_acct_inventory_valuations_part_id",
                table: "acct_inventory_valuations",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_inventory_valuations_book_part",
                table: "acct_inventory_valuations",
                columns: new[] { "book_id", "part_id" },
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
                name: "ix_acct_journal_template_lines_template",
                table: "acct_journal_template_lines",
                column: "journal_template_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_journal_templates_book_name",
                table: "acct_journal_templates",
                columns: new[] { "book_id", "name" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_acct_pay_run_lines_run",
                table: "acct_pay_run_lines",
                column: "pay_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_acct_pay_runs_book",
                table: "acct_pay_runs",
                column: "book_id");

            migrationBuilder.CreateIndex(
                name: "ux_acct_qbo_account_maps_gl_account",
                table: "acct_qbo_account_maps",
                column: "gl_account_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_acct_qbo_export_logs_book_range",
                table: "acct_qbo_export_logs",
                columns: new[] { "book_id", "from_date", "to_date" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_created_at",
                table: "activity_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_entity_type_entity_id",
                table: "activity_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_user_id",
                table: "activity_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_assistants_is_active_sort_order",
                table: "ai_assistants",
                columns: new[] { "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_acknowledged_by_id",
                table: "andon_alerts",
                column: "acknowledged_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_job_id",
                table: "andon_alerts",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_requested_at",
                table: "andon_alerts",
                column: "requested_at");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_requested_by_id",
                table: "andon_alerts",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_resolved_by_id",
                table: "andon_alerts",
                column: "resolved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_status",
                table: "andon_alerts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_andon_alerts_work_center_id",
                table: "andon_alerts",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_announcement_acknowledgments_announcement_id_user_id",
                table: "announcement_acknowledgments",
                columns: new[] { "announcement_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_announcement_acknowledgments_user_id",
                table: "announcement_acknowledgments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_announcement_teams_team_id",
                table: "announcement_teams",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_created_by_id",
                table: "announcements",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_department_id",
                table: "announcements",
                column: "department_id",
                filter: "department_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_severity_scope",
                table: "announcements",
                columns: new[] { "severity", "scope" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_template_id",
                table: "announcements",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_decisions_decided_by_id",
                table: "approval_decisions",
                column: "decided_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_decisions_delegated_to_user_id",
                table: "approval_decisions",
                column: "delegated_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_decisions_request_id",
                table: "approval_decisions",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_entity_type_entity_id",
                table: "approval_requests",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_requested_by_id",
                table: "approval_requests",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_status",
                table: "approval_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_workflow_id",
                table: "approval_requests",
                column: "workflow_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_steps_approver_user_id",
                table: "approval_steps",
                column: "approver_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_steps_workflow_id",
                table: "approval_steps",
                column: "workflow_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_workflows_entity_type",
                table: "approval_workflows",
                column: "entity_type");

            migrationBuilder.CreateIndex(
                name: "ix_approval_workflows_is_active",
                table: "approval_workflows",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_role_claims_role_id",
                table: "asp_net_role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "role_name_index",
                table: "asp_net_roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_claims_user_id",
                table: "asp_net_user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_logins_user_id",
                table: "asp_net_user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_roles_role_id",
                table: "asp_net_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "email_index",
                table: "asp_net_users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_role_template_id",
                table: "asp_net_users",
                column: "role_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_work_location_id",
                table: "asp_net_users",
                column: "work_location_id");

            migrationBuilder.CreateIndex(
                name: "user_name_index",
                table: "asp_net_users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_serial_number",
                table: "assets",
                column: "serial_number");

            migrationBuilder.CreateIndex(
                name: "ix_assets_source_job_id",
                table: "assets",
                column: "source_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_assets_source_part_id",
                table: "assets",
                column: "source_part_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_work_center_id",
                table: "assets",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_is_active_priority",
                table: "assignment_rules",
                columns: new[] { "is_active", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_action",
                table: "audit_log_entries",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_created_at",
                table: "audit_log_entries",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_entity_type_entity_id",
                table: "audit_log_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_user_id",
                table: "audit_log_entries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_po_suggestions_converted_purchase_order_id",
                table: "auto_po_suggestions",
                column: "converted_purchase_order_id",
                filter: "converted_purchase_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_auto_po_suggestions_part_id_status",
                table: "auto_po_suggestions",
                columns: new[] { "part_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_auto_po_suggestions_vendor_id",
                table: "auto_po_suggestions",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_asset_id",
                table: "barcodes",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_entity_type",
                table: "barcodes",
                column: "entity_type");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_job_id",
                table: "barcodes",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_part_id",
                table: "barcodes",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_purchase_order_id",
                table: "barcodes",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_sales_order_id",
                table: "barcodes",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_storage_location_id",
                table: "barcodes",
                column: "storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_user_id",
                table: "barcodes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_value",
                table: "barcodes",
                column: "value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bi_api_keys_is_active",
                table: "bi_api_keys",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_bi_api_keys_key_prefix",
                table: "bi_api_keys",
                column: "key_prefix");

            migrationBuilder.CreateIndex(
                name: "ix_bin_contents_job_id",
                table: "bin_contents",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_contents_location_id_entity_type_entity_id",
                table: "bin_contents",
                columns: new[] { "location_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bin_contents_uom_id",
                table: "bin_contents",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_movements_entity_type_entity_id",
                table: "bin_movements",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bin_movements_from_location_id",
                table: "bin_movements",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_movements_moved_at",
                table: "bin_movements",
                column: "moved_at");

            migrationBuilder.CreateIndex(
                name: "ix_bin_movements_reversed_movement_id",
                table: "bin_movements",
                column: "reversed_movement_id",
                filter: "reversed_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bin_movements_scan_action_log_id",
                table: "bin_movements",
                column: "scan_action_log_id",
                filter: "scan_action_log_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bin_movements_to_location_id",
                table: "bin_movements",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_bom_revision_lines_bom_revision_id",
                table: "bom_revision_lines",
                column: "bom_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_bom_revision_lines_part_id",
                table: "bom_revision_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_bom_revisions_part_id",
                table: "bom_revisions",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_bom_revisions_part_id_revision_number",
                table: "bom_revisions",
                columns: new[] { "part_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bomlines_child_part_id",
                table: "bomlines",
                column: "child_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_bomlines_parent_part_id",
                table: "bomlines",
                column: "parent_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_bomlines_uom_id",
                table: "bomlines",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_bomlines_vendor_id",
                table: "bomlines",
                column: "vendor_id",
                filter: "vendor_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_records_calibrated_at",
                table: "calibration_records",
                column: "calibrated_at");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_records_calibrated_by_id",
                table: "calibration_records",
                column: "calibrated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_records_certificate_file_id",
                table: "calibration_records",
                column: "certificate_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_records_gage_id",
                table: "calibration_records",
                column: "gage_id");

            migrationBuilder.CreateIndex(
                name: "ix_capa_tasks_assignee_id",
                table: "capa_tasks",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_capa_tasks_capa_id",
                table: "capa_tasks",
                column: "capa_id");

            migrationBuilder.CreateIndex(
                name: "ix_capa_tasks_completed_by_id",
                table: "capa_tasks",
                column: "completed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_capa_tasks_status",
                table: "capa_tasks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_capabilities_area",
                table: "capabilities",
                column: "area");

            migrationBuilder.CreateIndex(
                name: "ix_capabilities_code",
                table: "capabilities",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_capability_configs_capability_id",
                table: "capability_configs",
                column: "capability_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_message_mentions_chat_message_id",
                table: "chat_message_mentions",
                column: "chat_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_message_mentions_entity_type_entity_id",
                table: "chat_message_mentions",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_chat_room_id",
                table: "chat_messages",
                column: "chat_room_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_file_attachment_id",
                table: "chat_messages",
                column: "file_attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_parent_message_id",
                table: "chat_messages",
                column: "parent_message_id",
                filter: "parent_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_recipient_id",
                table: "chat_messages",
                column: "recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_sender_id",
                table: "chat_messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_sender_id_recipient_id_created_at",
                table: "chat_messages",
                columns: new[] { "sender_id", "recipient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_room_members_chat_room_id",
                table: "chat_room_members",
                column: "chat_room_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_room_members_chat_room_id_user_id",
                table: "chat_room_members",
                columns: new[] { "chat_room_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_room_members_last_read_message_id",
                table: "chat_room_members",
                column: "last_read_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_room_members_user_id",
                table: "chat_room_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_channel_type",
                table: "chat_rooms",
                column: "channel_type");

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_created_by_id",
                table: "chat_rooms",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_team_id",
                table: "chat_rooms",
                column: "team_id",
                filter: "team_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_clock_events_event_type_code",
                table: "clock_events",
                column: "event_type_code");

            migrationBuilder.CreateIndex(
                name: "ix_clock_events_operation_id",
                table: "clock_events",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_events_user_id_timestamp",
                table: "clock_events",
                columns: new[] { "user_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_cloud_storage_providers_provider_code_is_active",
                table: "cloud_storage_providers",
                columns: new[] { "provider_code", "is_active" },
                unique: true,
                filter: "is_active = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_communication_sync_configs_user_id",
                table: "communication_sync_configs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_communication_sync_configs_user_id_kind_provider_id",
                table: "communication_sync_configs",
                columns: new[] { "user_id", "kind", "provider_id" });

            migrationBuilder.CreateIndex(
                name: "ix_company_locations_is_default",
                table: "company_locations",
                column: "is_default",
                unique: true,
                filter: "is_default = true");

            migrationBuilder.CreateIndex(
                name: "ix_company_locations_state",
                table: "company_locations",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_company_locations_working_calendar_id",
                table: "company_locations",
                column: "working_calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_submissions_filled_pdf_file_id",
                table: "compliance_form_submissions",
                column: "filled_pdf_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_submissions_form_definition_version_id",
                table: "compliance_form_submissions",
                column: "form_definition_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_submissions_i9_employer_user_id",
                table: "compliance_form_submissions",
                column: "i9_employer_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_submissions_i9_reverification_due_at",
                table: "compliance_form_submissions",
                column: "i9_reverification_due_at");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_submissions_i9_section2_overdue_at",
                table: "compliance_form_submissions",
                column: "i9_section2_overdue_at");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_submissions_signed_pdf_file_id",
                table: "compliance_form_submissions",
                column: "signed_pdf_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_submissions_template_id",
                table: "compliance_form_submissions",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_submissions_user_id",
                table: "compliance_form_submissions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_submissions_user_id_template_id",
                table: "compliance_form_submissions",
                columns: new[] { "user_id", "template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_templates_filled_pdf_template_id",
                table: "compliance_form_templates",
                column: "filled_pdf_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_templates_form_type",
                table: "compliance_form_templates",
                column: "form_type");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_form_templates_manual_override_file_id",
                table: "compliance_form_templates",
                column: "manual_override_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_configurator_options_configurator_id",
                table: "configurator_options",
                column: "configurator_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_agreements_customer_id",
                table: "consignment_agreements",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_agreements_part_id",
                table: "consignment_agreements",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_agreements_status",
                table: "consignment_agreements",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_agreements_vendor_id",
                table: "consignment_agreements",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_transactions_agreement_id",
                table: "consignment_transactions",
                column: "agreement_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_transactions_invoice_id",
                table: "consignment_transactions",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_transactions_purchase_order_id",
                table: "consignment_transactions",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_interactions_contact_id",
                table: "contact_interactions",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_interactions_user_id",
                table: "contact_interactions",
                column: "user_id");

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
                name: "ix_contacts_customer_id",
                table: "contacts",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_controlled_documents_document_number",
                table: "controlled_documents",
                column: "document_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_controlled_documents_owner_id",
                table: "controlled_documents",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_controlled_documents_status",
                table: "controlled_documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_capa_number",
                table: "corrective_actions",
                column: "capa_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_closed_by_id",
                table: "corrective_actions",
                column: "closed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_due_date",
                table: "corrective_actions",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_effectiveness_checked_by_id",
                table: "corrective_actions",
                column: "effectiveness_checked_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_owner_id",
                table: "corrective_actions",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_priority",
                table: "corrective_actions",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_root_cause_analyzed_by_id",
                table: "corrective_actions",
                column: "root_cause_analyzed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_source_entity_id",
                table: "corrective_actions",
                column: "source_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_status",
                table: "corrective_actions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_verified_by_id",
                table: "corrective_actions",
                column: "verified_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_calculation_inputs_cost_calculation_id",
                table: "cost_calculation_inputs",
                column: "cost_calculation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_calculations_entity_type_entity_id",
                table: "cost_calculations",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cost_calculations_is_current",
                table: "cost_calculations",
                column: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_cost_calculations_profile_id",
                table: "cost_calculations",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_costing_profiles_code",
                table: "costing_profiles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_holds_customer_id",
                table: "credit_holds",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_holds_is_active",
                table: "credit_holds",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_credit_holds_placed_by_id",
                table: "credit_holds",
                column: "placed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_currencies_code",
                table: "currencies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_currencies_is_active",
                table: "currencies",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_customer_addresses_customer_id",
                table: "customer_addresses",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_portal_accesses_contact_id",
                table: "customer_portal_accesses",
                column: "contact_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customer_portal_accesses_customer_id",
                table: "customer_portal_accesses",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_portal_accesses_one_time_token_hash",
                table: "customer_portal_accesses",
                column: "one_time_token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_customer_returns_customer_id",
                table: "customer_returns",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_returns_original_job_id",
                table: "customer_returns",
                column: "original_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_returns_return_number",
                table: "customer_returns",
                column: "return_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_returns_rework_job_id",
                table: "customer_returns",
                column: "rework_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_returns_status",
                table: "customer_returns",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_customer_segments_name",
                table: "customer_segments",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_customers_credit_hold_by_id",
                table: "customers",
                column: "credit_hold_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_default_tax_code_id",
                table: "customers",
                column: "default_tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_is_tax_exempt",
                table: "customers",
                column: "is_tax_exempt");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_lines_bin_content_id",
                table: "cycle_count_lines",
                column: "bin_content_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_lines_cycle_count_id",
                table: "cycle_count_lines",
                column: "cycle_count_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_counted_by_id",
                table: "cycle_counts",
                column: "counted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_location_id_counted_at",
                table: "cycle_counts",
                columns: new[] { "location_id", "counted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deliverables_customer_id",
                table: "deliverables",
                column: "customer_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_deliverables_due_date",
                table: "deliverables",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_deliverables_job_id",
                table: "deliverables",
                column: "job_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_deliverables_project_id",
                table: "deliverables",
                column: "project_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_deliverables_status",
                table: "deliverables",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_applied_to_master_schedule_id",
                table: "demand_forecasts",
                column: "applied_to_master_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_created_by_user_id",
                table: "demand_forecasts",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_part_id",
                table: "demand_forecasts",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_status",
                table: "demand_forecasts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_discovery_runs_applied_preset_id",
                table: "discovery_runs",
                column: "applied_preset_id");

            migrationBuilder.CreateIndex(
                name: "ix_discovery_runs_completed_at",
                table: "discovery_runs",
                column: "completed_at");

            migrationBuilder.CreateIndex(
                name: "ix_discovery_runs_run_by_user_id",
                table: "discovery_runs",
                column: "run_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_embeddings_entity_type_entity_id",
                table: "document_embeddings",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_revisions_authored_by_id",
                table: "document_revisions",
                column: "authored_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_revisions_document_id",
                table: "document_revisions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_revisions_document_id_revision_number",
                table: "document_revisions",
                columns: new[] { "document_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_revisions_file_attachment_id",
                table: "document_revisions",
                column: "file_attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_domain_event_failures_event_type",
                table: "domain_event_failures",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_domain_event_failures_failed_at",
                table: "domain_event_failures",
                column: "failed_at");

            migrationBuilder.CreateIndex(
                name: "ix_domain_event_failures_status",
                table: "domain_event_failures",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_downtime_logs_asset_id",
                table: "downtime_logs",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_downtime_logs_job_id",
                table: "downtime_logs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_downtime_logs_started_at",
                table: "downtime_logs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_downtime_logs_work_center_id",
                table: "downtime_logs",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_eco_affected_items_eco_id",
                table: "eco_affected_items",
                column: "eco_id");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_integrations_default_customer_id",
                table: "ecommerce_integrations",
                column: "default_customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_integrations_is_active",
                table: "ecommerce_integrations",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_integrations_platform",
                table: "ecommerce_integrations",
                column: "platform");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_order_syncs_integration_id",
                table: "ecommerce_order_syncs",
                column: "integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_order_syncs_integration_id_external_order_id",
                table: "ecommerce_order_syncs",
                columns: new[] { "integration_id", "external_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_order_syncs_sales_order_id",
                table: "ecommerce_order_syncs",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_ecommerce_order_syncs_status",
                table: "ecommerce_order_syncs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_edi_mappings_trading_partner_id",
                table: "edi_mappings",
                column: "trading_partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_edi_mappings_trading_partner_id_transaction_set",
                table: "edi_mappings",
                columns: new[] { "trading_partner_id", "transaction_set" });

            migrationBuilder.CreateIndex(
                name: "ix_edi_trading_partners_customer_id",
                table: "edi_trading_partners",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_edi_trading_partners_is_active",
                table: "edi_trading_partners",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_edi_trading_partners_qualifier_id_qualifier_value",
                table: "edi_trading_partners",
                columns: new[] { "qualifier_id", "qualifier_value" });

            migrationBuilder.CreateIndex(
                name: "ix_edi_trading_partners_vendor_id",
                table: "edi_trading_partners",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_edi_transactions_acknowledgment_transaction_id",
                table: "edi_transactions",
                column: "acknowledgment_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_edi_transactions_direction",
                table: "edi_transactions",
                column: "direction");

            migrationBuilder.CreateIndex(
                name: "ix_edi_transactions_received_at",
                table: "edi_transactions",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_edi_transactions_related_entity_type_related_entity_id",
                table: "edi_transactions",
                columns: new[] { "related_entity_type", "related_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_edi_transactions_status",
                table: "edi_transactions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_edi_transactions_trading_partner_id",
                table: "edi_transactions",
                column: "trading_partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_edi_transactions_transaction_set",
                table: "edi_transactions",
                column: "transaction_set");

            migrationBuilder.CreateIndex(
                name: "ix_employee_profiles_user_id",
                table: "employee_profiles",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_engineering_change_orders_approved_by_id",
                table: "engineering_change_orders",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_engineering_change_orders_eco_number",
                table: "engineering_change_orders",
                column: "eco_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_engineering_change_orders_requested_by_id",
                table: "engineering_change_orders",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_engineering_change_orders_status",
                table: "engineering_change_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_entity_capability_requirements_entity_type",
                table: "entity_capability_requirements",
                column: "entity_type");

            migrationBuilder.CreateIndex(
                name: "ix_entity_capability_requirements_entity_type_capability_code_~",
                table: "entity_capability_requirements",
                columns: new[] { "entity_type", "capability_code", "requirement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_cloud_links_entity_type_entity_id",
                table: "entity_cloud_links",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_entity_cloud_links_entity_type_entity_id_provider_id",
                table: "entity_cloud_links",
                columns: new[] { "entity_type", "entity_id", "provider_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entity_cloud_links_provider_id",
                table: "entity_cloud_links",
                column: "provider_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_notes_created_by",
                table: "entity_notes",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_entity_notes_entity_type_entity_id",
                table: "entity_notes",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_entity_readiness_validators_entity_type",
                table: "entity_readiness_validators",
                column: "entity_type");

            migrationBuilder.CreateIndex(
                name: "ix_entity_readiness_validators_entity_type_validator_id",
                table: "entity_readiness_validators",
                columns: new[] { "entity_type", "validator_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_attendees_event_id_user_id",
                table: "event_attendees",
                columns: new[] { "event_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_attendees_user_id",
                table: "event_attendees",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_created_by_user_id",
                table: "events",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_start_time",
                table: "events",
                column: "start_time");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_effective_date",
                table: "exchange_rates",
                column: "effective_date");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_from_currency_id_to_currency_id_effective_da~",
                table: "exchange_rates",
                columns: new[] { "from_currency_id", "to_currency_id", "effective_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_to_currency_id",
                table: "exchange_rates",
                column: "to_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_job_id",
                table: "expenses",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_status",
                table: "expenses",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_user_id",
                table: "expenses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_vendor_id",
                table: "expenses",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_attachments_entity_type_entity_id",
                table: "file_attachments",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_file_attachments_part_revision_id",
                table: "file_attachments",
                column: "part_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_attachments_uploaded_by_id",
                table: "file_attachments",
                column: "uploaded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_fmea_analyses_fmea_number",
                table: "fmea_analyses",
                column: "fmea_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fmea_analyses_operation_id",
                table: "fmea_analyses",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_fmea_analyses_part_id",
                table: "fmea_analyses",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_fmea_analyses_ppap_submission_id",
                table: "fmea_analyses",
                column: "ppap_submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_fmea_analyses_status",
                table: "fmea_analyses",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_fmea_items_capa_id",
                table: "fmea_items",
                column: "capa_id");

            migrationBuilder.CreateIndex(
                name: "ix_fmea_items_fmea_id",
                table: "fmea_items",
                column: "fmea_id");

            migrationBuilder.CreateIndex(
                name: "ix_fmea_items_responsible_user_id",
                table: "fmea_items",
                column: "responsible_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_assigned_to_user_id",
                table: "follow_up_tasks",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_due_date",
                table: "follow_up_tasks",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_source_entity_type_source_entity_id",
                table: "follow_up_tasks",
                columns: new[] { "source_entity_type", "source_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_status",
                table: "follow_up_tasks",
                column: "status",
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_trigger_type",
                table: "follow_up_tasks",
                column: "trigger_type");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_overrides_demand_forecast_id",
                table: "forecast_overrides",
                column: "demand_forecast_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_definition_versions_state_code_effective_date",
                table: "form_definition_versions",
                columns: new[] { "state_code", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "ix_form_definition_versions_template_id_effective_date",
                table: "form_definition_versions",
                columns: new[] { "template_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "ix_gages_asset_id",
                table: "gages",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_gages_gage_number",
                table: "gages",
                column: "gage_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gages_location_id",
                table: "gages",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_gages_next_calibration_due",
                table: "gages",
                column: "next_calibration_due");

            migrationBuilder.CreateIndex(
                name: "ix_gages_status",
                table: "gages",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_holidays_working_calendar_id_date",
                table: "holidays",
                columns: new[] { "working_calendar_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_holidays_working_calendar_id_observed_date",
                table: "holidays",
                columns: new[] { "working_calendar_id", "observed_date" });

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
                name: "ix_identity_documents_file_attachment_id",
                table: "identity_documents",
                column: "file_attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_documents_user_id",
                table: "identity_documents",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_documents_verified_by_id",
                table: "identity_documents",
                column: "verified_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_outbox_entries_entity",
                table: "integration_outbox_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_outbox_entries_idempotency_key",
                table: "integration_outbox_entries",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_outbox_entries_provider_status",
                table: "integration_outbox_entries",
                columns: new[] { "provider", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_outbox_entries_status_next_attempt",
                table: "integration_outbox_entries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfer_lines_part_id",
                table: "inter_plant_transfer_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfer_lines_transfer_id",
                table: "inter_plant_transfer_lines",
                column: "transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_from_plant_id",
                table: "inter_plant_transfers",
                column: "from_plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_received_by_id",
                table: "inter_plant_transfers",
                column: "received_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_shipped_by_id",
                table: "inter_plant_transfers",
                column: "shipped_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_status",
                table: "inter_plant_transfers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_to_plant_id",
                table: "inter_plant_transfers",
                column: "to_plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_transfer_number",
                table: "inter_plant_transfers",
                column: "transfer_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id",
                table: "invoice_lines",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_part_id",
                table: "invoice_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_currency_id",
                table: "invoices",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_customer_id",
                table: "invoices",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_number",
                table: "invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_sales_order_id",
                table: "invoices",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_shipment_id",
                table: "invoices",
                column: "shipment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status",
                table: "invoices",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_job_activity_logs_created_at",
                table: "job_activity_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_job_activity_logs_job_id",
                table: "job_activity_logs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_activity_logs_operation_id",
                table: "job_activity_logs",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_activity_logs_work_center_id",
                table: "job_activity_logs",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_links_source_job_id",
                table: "job_links",
                column: "source_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_links_target_job_id",
                table: "job_links",
                column: "target_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_notes_created_by",
                table: "job_notes",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_job_notes_job_id",
                table: "job_notes",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_parts_job_id_part_id",
                table: "job_parts",
                columns: new[] { "job_id", "part_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_parts_job_id1",
                table: "job_parts",
                column: "job_id1");

            migrationBuilder.CreateIndex(
                name: "ix_job_parts_part_id",
                table: "job_parts",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_stages_track_type_id_code",
                table: "job_stages",
                columns: new[] { "track_type_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_stages_track_type_id_sort_order",
                table: "job_stages",
                columns: new[] { "track_type_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_job_subtasks_job_id",
                table: "job_subtasks",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_assignee_id",
                table: "jobs",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_bom_revision_id_at_release",
                table: "jobs",
                column: "bom_revision_id_at_release");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_cover_photo_file_id",
                table: "jobs",
                column: "cover_photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_current_stage_id",
                table: "jobs",
                column: "current_stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_customer_id",
                table: "jobs",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_due_date",
                table: "jobs",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_engagement_type_id",
                table: "jobs",
                column: "engagement_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_job_number",
                table: "jobs",
                column: "job_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_mrp_planned_order_id",
                table: "jobs",
                column: "mrp_planned_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_parent_job_id",
                table: "jobs",
                column: "parent_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_part_id",
                table: "jobs",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_project_phase_id",
                table: "jobs",
                column: "project_phase_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_sales_order_line_id",
                table: "jobs",
                column: "sales_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_sow_id",
                table: "jobs",
                column: "sow_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_track_type_id_current_stage_id",
                table: "jobs",
                columns: new[] { "track_type_id", "current_stage_id" });

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_card_number",
                table: "kanban_cards",
                column: "card_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_is_active",
                table: "kanban_cards",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_part_id",
                table: "kanban_cards",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_status",
                table: "kanban_cards",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_storage_location_id",
                table: "kanban_cards",
                column: "storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_supply_vendor_id",
                table: "kanban_cards",
                column: "supply_vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_cards_work_center_id",
                table: "kanban_cards",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_trigger_logs_kanban_card_id",
                table: "kanban_trigger_logs",
                column: "kanban_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_trigger_logs_triggered_at",
                table: "kanban_trigger_logs",
                column: "triggered_at");

            migrationBuilder.CreateIndex(
                name: "ix_kanban_trigger_logs_triggered_by_user_id",
                table: "kanban_trigger_logs",
                column: "triggered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_terminals_device_token",
                table: "kiosk_terminals",
                column: "device_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_terminals_team_id",
                table: "kiosk_terminals",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_terminals_work_center_id",
                table: "kiosk_terminals",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_labor_rates_user_id_effective_from",
                table: "labor_rates",
                columns: new[] { "user_id", "effective_from" });

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

            migrationBuilder.CreateIndex(
                name: "ix_lead_sources_code",
                table: "lead_sources",
                column: "code",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_leads_account_id",
                table: "leads",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_assigned_to_user_id",
                table: "leads",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_campaign_id",
                table: "leads",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_capability_fit",
                table: "leads",
                column: "capability_fit");

            migrationBuilder.CreateIndex(
                name: "ix_leads_converted_customer_id",
                table: "leads",
                column: "converted_customer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leads_icp_score",
                table: "leads",
                column: "icp_score");

            migrationBuilder.CreateIndex(
                name: "ix_leads_lead_source_id",
                table: "leads",
                column: "lead_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_outreach_state",
                table: "leads",
                column: "outreach_state");

            migrationBuilder.CreateIndex(
                name: "ix_leads_part_class_code",
                table: "leads",
                column: "part_class_code");

            migrationBuilder.CreateIndex(
                name: "ix_leads_secondary_owner_user_id",
                table: "leads",
                column: "secondary_owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_status",
                table: "leads",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balances_policy_id",
                table: "leave_balances",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balances_user_id",
                table: "leave_balances",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balances_user_id_policy_id",
                table: "leave_balances",
                columns: new[] { "user_id", "policy_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_approved_by_id",
                table: "leave_requests",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_policy_id",
                table: "leave_requests",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_status",
                table: "leave_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_user_id",
                table: "leave_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_lot_records_job_id",
                table: "lot_records",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_lot_records_lot_number",
                table: "lot_records",
                column: "lot_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lot_records_part_id",
                table: "lot_records",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_lot_records_production_run_id",
                table: "lot_records",
                column: "production_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_lot_records_purchase_order_line_id",
                table: "lot_records",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_connections_is_active",
                table: "machine_connections",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_machine_connections_work_center_id",
                table: "machine_connections",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_data_points_tag_id",
                table: "machine_data_points",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_data_points_tag_id_timestamp",
                table: "machine_data_points",
                columns: new[] { "tag_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_machine_data_points_timestamp",
                table: "machine_data_points",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_machine_data_points_work_center_id",
                table: "machine_data_points",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_tags_connection_id",
                table: "machine_tags",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_logs_maintenance_schedule_id",
                table: "maintenance_logs",
                column: "maintenance_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_predictions_acknowledged_by_user_id",
                table: "maintenance_predictions",
                column: "acknowledged_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_predictions_preventive_maintenance_job_id",
                table: "maintenance_predictions",
                column: "preventive_maintenance_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_predictions_severity",
                table: "maintenance_predictions",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_predictions_status",
                table: "maintenance_predictions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_predictions_work_center_id",
                table: "maintenance_predictions",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_schedules_asset_id",
                table: "maintenance_schedules",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_schedules_maintenance_job_id",
                table: "maintenance_schedules",
                column: "maintenance_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_schedules_next_due_at",
                table: "maintenance_schedules",
                column: "next_due_at");

            migrationBuilder.CreateIndex(
                name: "ix_master_schedule_lines_master_schedule_id",
                table: "master_schedule_lines",
                column: "master_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_master_schedule_lines_part_id",
                table: "master_schedule_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_master_schedules_created_by_user_id",
                table: "master_schedules",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_master_schedules_status",
                table: "master_schedules",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_bin_content_id",
                table: "material_issues",
                column: "bin_content_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_issued_by_id",
                table: "material_issues",
                column: "issued_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_job_id",
                table: "material_issues",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_operation_id",
                table: "material_issues",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_part_id",
                table: "material_issues",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_issues_storage_location_id",
                table: "material_issues",
                column: "storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_mfa_recovery_codes_user_id",
                table: "mfa_recovery_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mfa_recovery_codes_user_id_is_used",
                table: "mfa_recovery_codes",
                columns: new[] { "user_id", "is_used" });

            migrationBuilder.CreateIndex(
                name: "ix_ml_models_model_id",
                table: "ml_models",
                column: "model_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ml_models_status",
                table: "ml_models",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_ml_models_work_center_id",
                table: "ml_models",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_demands_mrp_run_id",
                table: "mrp_demands",
                column: "mrp_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_demands_parent_planned_order_id",
                table: "mrp_demands",
                column: "parent_planned_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_demands_part_id",
                table: "mrp_demands",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_exceptions_mrp_run_id",
                table: "mrp_exceptions",
                column: "mrp_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_exceptions_part_id",
                table: "mrp_exceptions",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_exceptions_resolved_by_user_id",
                table: "mrp_exceptions",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_mrp_run_id",
                table: "mrp_planned_orders",
                column: "mrp_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_parent_planned_order_id",
                table: "mrp_planned_orders",
                column: "parent_planned_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_part_id",
                table: "mrp_planned_orders",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_released_job_id",
                table: "mrp_planned_orders",
                column: "released_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_released_purchase_order_id",
                table: "mrp_planned_orders",
                column: "released_purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_status",
                table: "mrp_planned_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_runs_initiated_by_user_id",
                table: "mrp_runs",
                column: "initiated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_runs_run_number",
                table: "mrp_runs",
                column: "run_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mrp_runs_status",
                table: "mrp_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_supplies_mrp_run_id",
                table: "mrp_supplies",
                column: "mrp_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_supplies_part_id",
                table: "mrp_supplies",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_capa_id",
                table: "non_conformances",
                column: "capa_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_containment_by_id",
                table: "non_conformances",
                column: "containment_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_customer_id",
                table: "non_conformances",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_detected_by_id",
                table: "non_conformances",
                column: "detected_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_disposition_by_id",
                table: "non_conformances",
                column: "disposition_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_job_id",
                table: "non_conformances",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_ncr_number",
                table: "non_conformances",
                column: "ncr_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_part_id",
                table: "non_conformances",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_production_run_id",
                table: "non_conformances",
                column: "production_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_purchase_order_line_id",
                table: "non_conformances",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_qc_inspection_id",
                table: "non_conformances",
                column: "qc_inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_sales_order_line_id",
                table: "non_conformances",
                column: "sales_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_status",
                table: "non_conformances",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_type",
                table: "non_conformances",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformances_vendor_id",
                table: "non_conformances",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_sender_id",
                table: "notifications",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id",
                table: "notifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_is_dismissed_created_at",
                table: "notifications",
                columns: new[] { "user_id", "is_dismissed", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_oauth_state_tokens_token",
                table: "oauth_state_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_state_tokens_user_id",
                table: "oauth_state_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_operation_materials_bom_line_id",
                table: "operation_materials",
                column: "bom_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_operation_materials_operation_id",
                table: "operation_materials",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_operations_asset_id",
                table: "operations",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_operations_part_id",
                table: "operations",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_operations_referenced_operation_id",
                table: "operations",
                column: "referenced_operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_operations_subcontract_vendor_id",
                table: "operations",
                column: "subcontract_vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_operations_work_center_id",
                table: "operations",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_outreach_campaigns_is_active",
                table: "outreach_campaigns",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_outreach_campaigns_owner_user_id",
                table: "outreach_campaigns",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_rules_is_default",
                table: "overtime_rules",
                column: "is_default",
                unique: true,
                filter: "is_default = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_part_alternates_alternate_part_id",
                table: "part_alternates",
                column: "alternate_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_alternates_approved_by_id",
                table: "part_alternates",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_alternates_part_id_alternate_part_id",
                table: "part_alternates",
                columns: new[] { "part_id", "alternate_part_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_part_prices_part_id",
                table: "part_prices",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_prices_part_id_effective_to",
                table: "part_prices",
                columns: new[] { "part_id", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_part_purchase_units_content_uom_id",
                table: "part_purchase_units",
                column: "content_uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_purchase_units_part_id",
                table: "part_purchase_units",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_revisions_part_id",
                table: "part_revisions",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_revisions_part_id_revision",
                table: "part_revisions",
                columns: new[] { "part_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parts_current_bom_revision_id",
                table: "parts",
                column: "current_bom_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_current_cost_calculation_id",
                table: "parts",
                column: "current_cost_calculation_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_default_bin_id",
                table: "parts",
                column: "default_bin_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_item_kind_id",
                table: "parts",
                column: "item_kind_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_material_spec_id",
                table: "parts",
                column: "material_spec_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_name",
                table: "parts",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_parts_part_number",
                table: "parts",
                column: "part_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parts_preferred_vendor_id",
                table: "parts",
                column: "preferred_vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_procurement_source_inventory_class",
                table: "parts",
                columns: new[] { "procurement_source", "inventory_class" });

            migrationBuilder.CreateIndex(
                name: "ix_parts_purchase_uom_id",
                table: "parts",
                column: "purchase_uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_sales_uom_id",
                table: "parts",
                column: "sales_uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_source_part_id",
                table: "parts",
                column: "source_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_stock_uom_id",
                table: "parts",
                column: "stock_uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_tooling_asset_id",
                table: "parts",
                column: "tooling_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_valuation_class_id",
                table: "parts",
                column: "valuation_class_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_stub_deductions_pay_stub_id",
                table: "pay_stub_deductions",
                column: "pay_stub_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_stubs_external_id",
                table: "pay_stubs",
                column: "external_id",
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pay_stubs_file_attachment_id",
                table: "pay_stubs",
                column: "file_attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_stubs_user_id",
                table: "pay_stubs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_applications_invoice_id",
                table: "payment_applications",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_applications_payment_id",
                table: "payment_applications",
                column: "payment_id");

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
                name: "ix_payment_transmissions_source",
                table: "payment_transmissions",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_transmissions_status",
                table: "payment_transmissions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_payments_customer_id",
                table: "payments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_method",
                table: "payments",
                column: "method");

            migrationBuilder.CreateIndex(
                name: "ix_payments_payment_number",
                table: "payments",
                column: "payment_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_performance_reviews_cycle_id",
                table: "performance_reviews",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_performance_reviews_cycle_id_employee_id",
                table: "performance_reviews",
                columns: new[] { "cycle_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_performance_reviews_employee_id",
                table: "performance_reviews",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_performance_reviews_reviewer_id",
                table: "performance_reviews",
                column: "reviewer_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_from_location_id",
                table: "pick_lines",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_part_id",
                table: "pick_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_picked_by_user_id",
                table: "pick_lines",
                column: "picked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_shipment_line_id",
                table: "pick_lines",
                column: "shipment_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_lines_wave_id",
                table: "pick_lines",
                column: "wave_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_assigned_to_id",
                table: "pick_waves",
                column: "assigned_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_status",
                table: "pick_waves",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_wave_number",
                table: "pick_waves",
                column: "wave_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_cycle_entries_job_id",
                table: "planning_cycle_entries",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_planning_cycle_entries_planning_cycle_id_job_id",
                table: "planning_cycle_entries",
                columns: new[] { "planning_cycle_id", "job_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_cycles_status",
                table: "planning_cycles",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_plants_code",
                table: "plants",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plants_company_location_id",
                table: "plants",
                column: "company_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_plants_is_active",
                table: "plants",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_ppap_elements_assigned_to_user_id",
                table: "ppap_elements",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ppap_elements_submission_id_element_number",
                table: "ppap_elements",
                columns: new[] { "submission_id", "element_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ppap_submissions_customer_id",
                table: "ppap_submissions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_ppap_submissions_part_id",
                table: "ppap_submissions",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_ppap_submissions_psw_signed_by_user_id",
                table: "ppap_submissions",
                column: "psw_signed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ppap_submissions_status",
                table: "ppap_submissions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_ppap_submissions_submission_number",
                table: "ppap_submissions",
                column: "submission_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prediction_feedbacks_prediction_id",
                table: "prediction_feedbacks",
                column: "prediction_id");

            migrationBuilder.CreateIndex(
                name: "ix_prediction_feedbacks_recorded_by_user_id",
                table: "prediction_feedbacks",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_entries_part_id",
                table: "price_list_entries",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_entries_price_list_id",
                table: "price_list_entries",
                column: "price_list_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_entries_price_list_id_part_id_min_quantity",
                table: "price_list_entries",
                columns: new[] { "price_list_id", "part_id", "min_quantity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_customer_id",
                table: "price_lists",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurations_configuration_code",
                table: "product_configurations",
                column: "configuration_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_configurations_configurator_id",
                table: "product_configurations",
                column: "configurator_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurations_part_id",
                table: "product_configurations",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurations_quote_id",
                table: "product_configurations",
                column: "quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurators_base_part_id",
                table: "product_configurators",
                column: "base_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurators_is_active",
                table: "product_configurators",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_production_runs_job_id",
                table: "production_runs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_runs_part_id",
                table: "production_runs",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_runs_run_number",
                table: "production_runs",
                column: "run_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_runs_status",
                table: "production_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_production_runs_work_center_id",
                table: "production_runs",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_customer_id",
                table: "projects",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_project_number",
                table: "projects",
                column: "project_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_sales_order_id",
                table: "projects",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_status",
                table: "projects",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_mrp_planned_order_id",
                table: "purchase_order_lines",
                column: "mrp_planned_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_part_id",
                table: "purchase_order_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id",
                table: "purchase_order_lines",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_unit_id",
                table: "purchase_order_lines",
                column: "purchase_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_uom_id",
                table: "purchase_order_lines",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_releases_purchase_order_id_release_number",
                table: "purchase_order_releases",
                columns: new[] { "purchase_order_id", "release_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_releases_purchase_order_line_id",
                table: "purchase_order_releases",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_releases_receiving_record_id",
                table: "purchase_order_releases",
                column: "receiving_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_releases_status",
                table: "purchase_order_releases",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_job_id",
                table: "purchase_orders",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_ponumber",
                table: "purchase_orders",
                column: "ponumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_status",
                table: "purchase_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_vendor_id",
                table: "purchase_orders",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_qc_checklist_items_template_id",
                table: "qc_checklist_items",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_qc_checklist_templates_part_id",
                table: "qc_checklist_templates",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_qc_inspection_results_checklist_item_id",
                table: "qc_inspection_results",
                column: "checklist_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_qc_inspection_results_inspection_id",
                table: "qc_inspection_results",
                column: "inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_qc_inspections_inspector_id",
                table: "qc_inspections",
                column: "inspector_id");

            migrationBuilder.CreateIndex(
                name: "ix_qc_inspections_job_id",
                table: "qc_inspections",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_qc_inspections_production_run_id",
                table: "qc_inspections",
                column: "production_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_qc_inspections_template_id",
                table: "qc_inspections",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_part_id",
                table: "quote_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_quote_id",
                table: "quote_lines",
                column: "quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_assigned_to_id",
                table: "quotes",
                column: "assigned_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_customer_id",
                table: "quotes",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_quote_number",
                table: "quotes",
                column: "quote_number",
                unique: true,
                filter: "quote_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_shipping_address_id",
                table: "quotes",
                column: "shipping_address_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_source_estimate_id",
                table: "quotes",
                column: "source_estimate_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotes_status",
                table: "quotes",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_type",
                table: "quotes",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_inspections_inspected_by_id",
                table: "receiving_inspections",
                column: "inspected_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_inspections_qc_inspection_id",
                table: "receiving_inspections",
                column: "qc_inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_inspections_receiving_record_id",
                table: "receiving_inspections",
                column: "receiving_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_records_purchase_order_line_id",
                table: "receiving_records",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_records_receipt_number",
                table: "receiving_records",
                column: "receipt_number");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_records_storage_location_id",
                table: "receiving_records",
                column: "storage_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_expenses_classification",
                table: "recurring_expenses",
                column: "classification");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_expenses_is_active",
                table: "recurring_expenses",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_expenses_next_occurrence_date",
                table: "recurring_expenses",
                column: "next_occurrence_date");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_expenses_user_id",
                table: "recurring_expenses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_order_lines_part_id",
                table: "recurring_order_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_order_lines_recurring_order_id",
                table: "recurring_order_lines",
                column: "recurring_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_orders_customer_id",
                table: "recurring_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_orders_next_generation_date",
                table: "recurring_orders",
                column: "next_generation_date");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_orders_shipping_address_id",
                table: "recurring_orders",
                column: "shipping_address_id");

            migrationBuilder.CreateIndex(
                name: "ix_reference_data_group_code_code",
                table: "reference_data",
                columns: new[] { "group_code", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reference_data_parent_id",
                table: "reference_data",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_reorder_suggestions_part_id",
                table: "reorder_suggestions",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_reorder_suggestions_part_id_status",
                table: "reorder_suggestions",
                columns: new[] { "part_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reorder_suggestions_resulting_purchase_order_id",
                table: "reorder_suggestions",
                column: "resulting_purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_reorder_suggestions_status",
                table: "reorder_suggestions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_reorder_suggestions_vendor_id",
                table: "reorder_suggestions",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_is_active",
                table: "report_schedules",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_next_run_at",
                table: "report_schedules",
                column: "next_run_at");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_saved_report_id",
                table: "report_schedules",
                column: "saved_report_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_for_quotes_part_id",
                table: "request_for_quotes",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_for_quotes_rfq_number",
                table: "request_for_quotes",
                column: "rfq_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_request_for_quotes_status",
                table: "request_for_quotes",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_bin_content_id",
                table: "reservations",
                column: "bin_content_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_job_id",
                table: "reservations",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_part_id",
                table: "reservations",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_sales_order_line_id",
                table: "reservations",
                column: "sales_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_vendor_responses_rfq_id_vendor_id",
                table: "rfq_vendor_responses",
                columns: new[] { "rfq_id", "vendor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rfq_vendor_responses_vendor_id",
                table: "rfq_vendor_responses",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_templates_name",
                table: "role_templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_part_id",
                table: "sales_order_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_sales_order_id",
                table: "sales_order_lines",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_uom_id",
                table: "sales_order_lines",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_billing_address_id",
                table: "sales_orders",
                column: "billing_address_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_customer_id",
                table: "sales_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_order_number",
                table: "sales_orders",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_quote_id",
                table: "sales_orders",
                column: "quote_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_shipping_address_id",
                table: "sales_orders",
                column: "shipping_address_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_status",
                table: "sales_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_sales_tax_rates_code",
                table: "sales_tax_rates",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_tax_rates_state_code",
                table: "sales_tax_rates",
                column: "state_code");

            migrationBuilder.CreateIndex(
                name: "ix_sales_tax_rates_state_code_effective_to",
                table: "sales_tax_rates",
                columns: new[] { "state_code", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_sample_shipments_lead_id",
                table: "sample_shipments",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_sample_shipments_status",
                table: "sample_shipments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_saved_reports_is_shared",
                table: "saved_reports",
                column: "is_shared");

            migrationBuilder.CreateIndex(
                name: "ix_saved_reports_user_id",
                table: "saved_reports",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_action_type",
                table: "scan_action_logs",
                column: "action_type");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_created_at",
                table: "scan_action_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_from_location_id",
                table: "scan_action_logs",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_part_id",
                table: "scan_action_logs",
                column: "part_id",
                filter: "part_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_reversed_by_log_id",
                table: "scan_action_logs",
                column: "reversed_by_log_id",
                filter: "reversed_by_log_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_reverses_log_id",
                table: "scan_action_logs",
                column: "reverses_log_id",
                filter: "reverses_log_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_to_location_id",
                table: "scan_action_logs",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_action_logs_user_id",
                table: "scan_action_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_milestones_job_id",
                table: "schedule_milestones",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_milestones_milestone_type",
                table: "schedule_milestones",
                column: "milestone_type");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_milestones_sales_order_line_id",
                table: "schedule_milestones",
                column: "sales_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_job_id",
                table: "scheduled_operations",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_operation_id",
                table: "scheduled_operations",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_schedule_run_id",
                table: "scheduled_operations",
                column: "schedule_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_work_center_id",
                table: "scheduled_operations",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_operations_work_center_id_scheduled_start",
                table: "scheduled_operations",
                columns: new[] { "work_center_id", "scheduled_start" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_tasks_internal_project_type_id",
                table: "scheduled_tasks",
                column: "internal_project_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_tasks_is_active",
                table: "scheduled_tasks",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_tasks_next_run_at",
                table: "scheduled_tasks",
                column: "next_run_at");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_tasks_track_type_id",
                table: "scheduled_tasks",
                column: "track_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_histories_actor_id",
                table: "serial_histories",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_histories_occurred_at",
                table: "serial_histories",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_serial_histories_serial_number_id",
                table: "serial_histories",
                column: "serial_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_current_location_id",
                table: "serial_numbers",
                column: "current_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_customer_id",
                table: "serial_numbers",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_job_id",
                table: "serial_numbers",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_lot_record_id",
                table: "serial_numbers",
                column: "lot_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_parent_serial_id",
                table: "serial_numbers",
                column: "parent_serial_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_part_id",
                table: "serial_numbers",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_serial_value",
                table: "serial_numbers",
                column: "serial_value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_shipment_line_id",
                table: "serial_numbers",
                column: "shipment_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_status",
                table: "serial_numbers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_shift_assignments_shift_id",
                table: "shift_assignments",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_assignments_user_id",
                table: "shift_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_assignments_user_id_effective_from",
                table: "shift_assignments",
                columns: new[] { "user_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_shifts_working_calendar_id",
                table: "shifts",
                column: "working_calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_lines_part_id",
                table: "shipment_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_lines_sales_order_line_id",
                table: "shipment_lines",
                column: "sales_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_lines_shipment_id",
                table: "shipment_lines",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_packages_shipment_id",
                table: "shipment_packages",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_sales_order_id",
                table: "shipments",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_shipment_number",
                table: "shipments",
                column: "shipment_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_shipping_address_id",
                table: "shipments",
                column: "shipping_address_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_status",
                table: "shipments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_spc_characteristics_gage_id",
                table: "spc_characteristics",
                column: "gage_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_characteristics_operation_id",
                table: "spc_characteristics",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_characteristics_part_id",
                table: "spc_characteristics",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_control_limits_characteristic_id",
                table: "spc_control_limits",
                column: "characteristic_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_control_limits_characteristic_id_is_active",
                table: "spc_control_limits",
                columns: new[] { "characteristic_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_spc_measurements_characteristic_id",
                table: "spc_measurements",
                column: "characteristic_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_measurements_characteristic_id_subgroup_number",
                table: "spc_measurements",
                columns: new[] { "characteristic_id", "subgroup_number" });

            migrationBuilder.CreateIndex(
                name: "ix_spc_measurements_job_id",
                table: "spc_measurements",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_measurements_measured_by_id",
                table: "spc_measurements",
                column: "measured_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_measurements_production_run_id",
                table: "spc_measurements",
                column: "production_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_ooc_events_acknowledged_by_id",
                table: "spc_ooc_events",
                column: "acknowledged_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_ooc_events_capa_id",
                table: "spc_ooc_events",
                column: "capa_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_ooc_events_characteristic_id",
                table: "spc_ooc_events",
                column: "characteristic_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_ooc_events_measurement_id",
                table: "spc_ooc_events",
                column: "measurement_id");

            migrationBuilder.CreateIndex(
                name: "ix_spc_ooc_events_status",
                table: "spc_ooc_events",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_status_entries_entity_type_entity_id",
                table: "status_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_status_entries_entity_type_entity_id_category",
                table: "status_entries",
                columns: new[] { "entity_type", "entity_id", "category" });

            migrationBuilder.CreateIndex(
                name: "ix_status_entries_entity_type_entity_id_ended_at",
                table: "status_entries",
                columns: new[] { "entity_type", "entity_id", "ended_at" });

            migrationBuilder.CreateIndex(
                name: "ix_status_entries_operation_id",
                table: "status_entries",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_status_entries_set_by_id",
                table: "status_entries",
                column: "set_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_status_entries_work_center_id",
                table: "status_entries",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_locations_barcode",
                table: "storage_locations",
                column: "barcode",
                unique: true,
                filter: "barcode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_storage_locations_parent_id",
                table: "storage_locations",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_job_id",
                table: "subcontract_orders",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_operation_id",
                table: "subcontract_orders",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_purchase_order_id",
                table: "subcontract_orders",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_status",
                table: "subcontract_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_subcontract_orders_vendor_id",
                table: "subcontract_orders",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_supported_languages_code",
                table: "supported_languages",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supported_languages_is_active",
                table: "supported_languages",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_sync_queue_entries_entity_type_entity_id",
                table: "sync_queue_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_queue_entries_status_created_at",
                table: "sync_queue_entries",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_system_api_keys_is_active",
                table: "system_api_keys",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_system_api_keys_key_prefix",
                table: "system_api_keys",
                column: "key_prefix");

            migrationBuilder.CreateIndex(
                name: "ix_system_api_keys_role_template_id",
                table: "system_api_keys",
                column: "role_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_system_api_keys_user_id",
                table: "system_api_keys",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_system_settings_key",
                table: "system_settings",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tariff_rates_hts_code_country_of_origin_effective_from",
                table: "tariff_rates",
                columns: new[] { "hts_code", "country_of_origin", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_documents_external_id",
                table: "tax_documents",
                column: "external_id",
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_documents_file_attachment_id",
                table: "tax_documents",
                column: "file_attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_documents_user_id_tax_year",
                table: "tax_documents",
                columns: new[] { "user_id", "tax_year" });

            migrationBuilder.CreateIndex(
                name: "ix_terminology_entries_key",
                table: "terminology_entries",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terminology_entries_source_preset_id",
                table: "terminology_entries",
                column: "source_preset_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_correction_logs_corrected_by_user_id",
                table: "time_correction_logs",
                column: "corrected_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_correction_logs_time_entry_id",
                table: "time_correction_logs",
                column: "time_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_activity_type_id",
                table: "time_entries",
                column: "activity_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_job_id",
                table: "time_entries",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_operation_id",
                table: "time_entries",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_user_id_date",
                table: "time_entries",
                columns: new[] { "user_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_work_center_id",
                table: "time_entries",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_types_code",
                table: "track_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_training_modules_created_by_user_id",
                table: "training_modules",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_training_modules_is_onboarding_required",
                table: "training_modules",
                column: "is_onboarding_required");

            migrationBuilder.CreateIndex(
                name: "ix_training_modules_is_published",
                table: "training_modules",
                column: "is_published");

            migrationBuilder.CreateIndex(
                name: "ix_training_modules_slug",
                table: "training_modules",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_training_path_enrollments_assigned_by_user_id",
                table: "training_path_enrollments",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_training_path_enrollments_path_id",
                table: "training_path_enrollments",
                column: "path_id");

            migrationBuilder.CreateIndex(
                name: "ix_training_path_enrollments_user_id",
                table: "training_path_enrollments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_training_path_enrollments_user_id_path_id",
                table: "training_path_enrollments",
                columns: new[] { "user_id", "path_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_training_path_modules_module_id",
                table: "training_path_modules",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_training_path_modules_path_id_position",
                table: "training_path_modules",
                columns: new[] { "path_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_training_paths_slug",
                table: "training_paths",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_training_progress_module_id",
                table: "training_progress",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_training_progress_status",
                table: "training_progress",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_training_progress_user_id",
                table: "training_progress",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_training_progress_user_id_module_id",
                table: "training_progress",
                columns: new[] { "user_id", "module_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_training_scan_logs_job_id",
                table: "training_scan_logs",
                column: "job_id",
                filter: "job_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_training_scan_logs_part_id",
                table: "training_scan_logs",
                column: "part_id",
                filter: "part_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_training_scan_logs_user_id_scanned_at",
                table: "training_scan_logs",
                columns: new[] { "user_id", "scanned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_translated_labels_key_language_code",
                table: "translated_labels",
                columns: new[] { "key", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translated_labels_language_code",
                table: "translated_labels",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_translated_labels_translated_by_id",
                table: "translated_labels",
                column: "translated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_of_measure_category",
                table: "units_of_measure",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_units_of_measure_code",
                table: "units_of_measure",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_uom_conversions_from_uom_id_to_uom_id_part_id",
                table: "uom_conversions",
                columns: new[] { "from_uom_id", "to_uom_id", "part_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_uom_conversions_part_id",
                table: "uom_conversions",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_uom_conversions_to_uom_id",
                table: "uom_conversions",
                column: "to_uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_cloud_storage_links_provider_id",
                table: "user_cloud_storage_links",
                column: "provider_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_cloud_storage_links_user_id_provider_id",
                table: "user_cloud_storage_links",
                columns: new[] { "user_id", "provider_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_integrations_user_id",
                table: "user_integrations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_integrations_user_id_provider_id",
                table: "user_integrations",
                columns: new[] { "user_id", "provider_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_mfa_devices_user_id",
                table: "user_mfa_devices",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_mfa_devices_user_id_is_default",
                table: "user_mfa_devices",
                columns: new[] { "user_id", "is_default" },
                unique: true,
                filter: "is_default = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_preferences_user_id_key",
                table: "user_preferences",
                columns: new[] { "user_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_scan_devices_device_id",
                table: "user_scan_devices",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_scan_devices_user_id",
                table: "user_scan_devices",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_scan_identifiers_identifier_type_identifier_value",
                table: "user_scan_identifiers",
                columns: new[] { "identifier_type", "identifier_value" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_scan_identifiers_user_id",
                table: "user_scan_identifiers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bank_accounts_status",
                table: "vendor_bank_accounts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bank_accounts_vendor",
                table: "vendor_bank_accounts",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bill_lines_job",
                table: "vendor_bill_lines",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bill_lines_part",
                table: "vendor_bill_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bill_lines_po_line",
                table: "vendor_bill_lines",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bill_lines_vendor_bill_id",
                table: "vendor_bill_lines",
                column: "vendor_bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bills_currency",
                table: "vendor_bills",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bills_po",
                table: "vendor_bills",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bills_status",
                table: "vendor_bills",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bills_vendor",
                table: "vendor_bills",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_vendor_bills_expense_live",
                table: "vendor_bills",
                column: "expense_id",
                unique: true,
                filter: "expense_id IS NOT NULL AND status <> 4");

            migrationBuilder.CreateIndex(
                name: "ux_vendor_bills_number",
                table: "vendor_bills",
                column: "bill_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_vendor_bills_vendor_invoice",
                table: "vendor_bills",
                columns: new[] { "vendor_id", "vendor_invoice_number" },
                unique: true,
                filter: "vendor_invoice_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_part_price_tiers_purchase_unit_id",
                table: "vendor_part_price_tiers",
                column: "purchase_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_part_price_tiers_vendor_part_id_effective_from",
                table: "vendor_part_price_tiers",
                columns: new[] { "vendor_part_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_part_price_tiers_vendor_part_id_min_quantity",
                table: "vendor_part_price_tiers",
                columns: new[] { "vendor_part_id", "min_quantity" });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_parts_part_id",
                table: "vendor_parts",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_parts_vendor_id_part_id",
                table: "vendor_parts",
                columns: new[] { "vendor_id", "part_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_parts_vendor_mpn",
                table: "vendor_parts",
                column: "vendor_mpn");

            migrationBuilder.CreateIndex(
                name: "ix_vpa_bill",
                table: "vendor_payment_applications",
                column: "vendor_bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_vpa_payment",
                table: "vendor_payment_applications",
                column: "vendor_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_payments_vendor",
                table: "vendor_payments",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ux_vendor_payments_number",
                table: "vendor_payments",
                column: "payment_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_scorecards_vendor_id_period_start",
                table: "vendor_scorecards",
                columns: new[] { "vendor_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ix_vendors_company_name",
                table: "vendors",
                column: "company_name");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_cost_entries_category",
                table: "wbs_cost_entries",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_cost_entries_entry_date",
                table: "wbs_cost_entries",
                column: "entry_date");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_cost_entries_source_entity_type_source_entity_id",
                table: "wbs_cost_entries",
                columns: new[] { "source_entity_type", "source_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_wbs_cost_entries_wbs_element_id",
                table: "wbs_cost_entries",
                column: "wbs_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_elements_parent_element_id",
                table: "wbs_elements",
                column: "parent_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_elements_project_id",
                table: "wbs_elements",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_wbs_elements_project_id_code",
                table: "wbs_elements",
                columns: new[] { "project_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_attempted_at",
                table: "webhook_deliveries",
                column: "attempted_at");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_is_success",
                table: "webhook_deliveries",
                column: "is_success");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_subscription_id",
                table: "webhook_deliveries",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_subscriptions_is_active",
                table: "webhook_subscriptions",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_work_center_calendars_work_center_id_date",
                table: "work_center_calendars",
                columns: new[] { "work_center_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_center_qualifications_qualified_by_id",
                table: "work_center_qualifications",
                column: "qualified_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_center_qualifications_work_center_id",
                table: "work_center_qualifications",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_center_shifts_shift_id",
                table: "work_center_shifts",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_center_shifts_work_center_id_shift_id",
                table: "work_center_shifts",
                columns: new[] { "work_center_id", "shift_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_centers_asset_id",
                table: "work_centers",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_centers_code",
                table: "work_centers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_centers_company_location_id",
                table: "work_centers",
                column: "company_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_definition_id",
                table: "workflow_definitions",
                column: "definition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_entity_type",
                table: "workflow_definitions",
                column: "entity_type");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_run_entities_entity_type_entity_id",
                table: "workflow_run_entities",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_definition_id",
                table: "workflow_runs",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_entity_type_entity_id",
                table: "workflow_runs",
                columns: new[] { "entity_type", "entity_id" },
                unique: true,
                filter: "\"entity_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_last_activity_at",
                table: "workflow_runs",
                column: "last_activity_at");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_started_by_user_id",
                table: "workflow_runs",
                column: "started_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_working_calendars_is_default",
                table: "working_calendars",
                column: "is_default",
                unique: true,
                filter: "is_default = true");

            migrationBuilder.CreateIndex(
                name: "ix_working_calendars_name",
                table: "working_calendars",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_abc_classifications__parts_part_id",
                table: "abc_classifications",
                column: "part_id",
                principalTable: "parts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_acct_bank_rec_items_line",
                table: "acct_bank_reconciliation_items",
                column: "journal_line_id",
                principalTable: "acct_journal_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_acct_stmt_lines_journal_line",
                table: "acct_bank_statement_lines",
                column: "matched_journal_line_id",
                principalTable: "acct_journal_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_acct_depreciation_entries_asset",
                table: "acct_depreciation_entries",
                column: "fixed_asset_id",
                principalTable: "acct_fixed_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_acct_fixed_assets_linked_asset",
                table: "acct_fixed_assets",
                column: "linked_asset_id",
                principalTable: "assets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_acct_inventory_valuations_part",
                table: "acct_inventory_valuations",
                column: "part_id",
                principalTable: "parts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_acct_journal_lines_job",
                table: "acct_journal_lines",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_andon_alerts__jobs_job_id",
                table: "andon_alerts",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_andon_alerts__work_centers_work_center_id",
                table: "andon_alerts",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_assets__jobs_source_job_id",
                table: "assets",
                column: "source_job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_assets__parts_source_part_id",
                table: "assets",
                column: "source_part_id",
                principalTable: "parts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_assets__work_centers_work_center_id",
                table: "assets",
                column: "work_center_id",
                principalTable: "work_centers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_auto_po_suggestions__parts_part_id",
                table: "auto_po_suggestions",
                column: "part_id",
                principalTable: "parts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_auto_po_suggestions__purchase_orders_converted_purchase_order~",
                table: "auto_po_suggestions",
                column: "converted_purchase_order_id",
                principalTable: "purchase_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_barcodes__jobs_job_id",
                table: "barcodes",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_barcodes__parts_part_id",
                table: "barcodes",
                column: "part_id",
                principalTable: "parts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_barcodes__purchase_orders_purchase_order_id",
                table: "barcodes",
                column: "purchase_order_id",
                principalTable: "purchase_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_bin_contents__jobs_job_id",
                table: "bin_contents",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_bin_movements__scan_action_logs_scan_action_log_id",
                table: "bin_movements",
                column: "scan_action_log_id",
                principalTable: "scan_action_logs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bom_revision_lines__parts_part_id",
                table: "bom_revision_lines",
                column: "part_id",
                principalTable: "parts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bom_revision_lines_bom_revisions_bom_revision_id",
                table: "bom_revision_lines",
                column: "bom_revision_id",
                principalTable: "bom_revisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_bom_revisions__parts_part_id",
                table: "bom_revisions",
                column: "part_id",
                principalTable: "parts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_consignment_transactions__purchase_orders_purchase_order_id",
                table: "consignment_transactions",
                column: "purchase_order_id",
                principalTable: "purchase_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_returns__jobs_original_job_id",
                table: "customer_returns",
                column: "original_job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_returns__jobs_rework_job_id",
                table: "customer_returns",
                column: "rework_job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_deliverables__jobs_job_id",
                table: "deliverables",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_downtime_logs__jobs_job_id",
                table: "downtime_logs",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_expenses__jobs_job_id",
                table: "expenses",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_job_activity_logs_jobs_job_id",
                table: "job_activity_logs",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_job_links_jobs_source_job_id",
                table: "job_links",
                column: "source_job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_job_links_jobs_target_job_id",
                table: "job_links",
                column: "target_job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_job_notes_jobs_job_id",
                table: "job_notes",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_job_parts_jobs_job_id",
                table: "job_parts",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_job_parts_jobs_job_id1",
                table: "job_parts",
                column: "job_id1",
                principalTable: "jobs",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_job_subtasks_jobs_job_id",
                table: "job_subtasks",
                column: "job_id",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_jobs__mrp_planned_orders_mrp_planned_order_id",
                table: "jobs",
                column: "mrp_planned_order_id",
                principalTable: "mrp_planned_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assets__parts_source_part_id",
                table: "assets");

            migrationBuilder.DropForeignKey(
                name: "fk_bom_revisions__parts_part_id",
                table: "bom_revisions");

            migrationBuilder.DropForeignKey(
                name: "fk_jobs__parts_part_id",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "fk_mrp_planned_orders__parts_part_id",
                table: "mrp_planned_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_part_revisions_parts_part_id",
                table: "part_revisions");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_order_lines_parts_part_id",
                table: "sales_order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_work_centers_assets_asset_id",
                table: "work_centers");

            migrationBuilder.DropForeignKey(
                name: "fk_mrp_planned_orders_jobs_released_job_id",
                table: "mrp_planned_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_orders_jobs_job_id",
                table: "purchase_orders");

            migrationBuilder.DropTable(
                name: "abc_classifications");

            migrationBuilder.DropTable(
                name: "account_contacts");

            migrationBuilder.DropTable(
                name: "acct_account_determination_rules");

            migrationBuilder.DropTable(
                name: "acct_ap_open_items");

            migrationBuilder.DropTable(
                name: "acct_ar_open_items");

            migrationBuilder.DropTable(
                name: "acct_bank_reconciliation_items");

            migrationBuilder.DropTable(
                name: "acct_bank_statement_lines");

            migrationBuilder.DropTable(
                name: "acct_depreciation_entries");

            migrationBuilder.DropTable(
                name: "acct_inventory_valuations");

            migrationBuilder.DropTable(
                name: "acct_journal_template_lines");

            migrationBuilder.DropTable(
                name: "acct_ledger_balances");

            migrationBuilder.DropTable(
                name: "acct_number_sequences");

            migrationBuilder.DropTable(
                name: "acct_pay_run_lines");

            migrationBuilder.DropTable(
                name: "acct_qbo_account_maps");

            migrationBuilder.DropTable(
                name: "acct_qbo_export_logs");

            migrationBuilder.DropTable(
                name: "activity_logs");

            migrationBuilder.DropTable(
                name: "ai_assistants");

            migrationBuilder.DropTable(
                name: "andon_alerts");

            migrationBuilder.DropTable(
                name: "announcement_acknowledgments");

            migrationBuilder.DropTable(
                name: "announcement_teams");

            migrationBuilder.DropTable(
                name: "approval_decisions");

            migrationBuilder.DropTable(
                name: "approval_steps");

            migrationBuilder.DropTable(
                name: "asp_net_role_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_logins");

            migrationBuilder.DropTable(
                name: "asp_net_user_roles");

            migrationBuilder.DropTable(
                name: "asp_net_user_tokens");

            migrationBuilder.DropTable(
                name: "assignment_rules");

            migrationBuilder.DropTable(
                name: "audit_log_entries");

            migrationBuilder.DropTable(
                name: "auto_po_suggestions");

            migrationBuilder.DropTable(
                name: "barcodes");

            migrationBuilder.DropTable(
                name: "bi_api_keys");

            migrationBuilder.DropTable(
                name: "bin_movements");

            migrationBuilder.DropTable(
                name: "bom_revision_lines");

            migrationBuilder.DropTable(
                name: "calibration_records");

            migrationBuilder.DropTable(
                name: "capa_tasks");

            migrationBuilder.DropTable(
                name: "capability_configs");

            migrationBuilder.DropTable(
                name: "chat_message_mentions");

            migrationBuilder.DropTable(
                name: "chat_room_members");

            migrationBuilder.DropTable(
                name: "clock_events");

            migrationBuilder.DropTable(
                name: "communication_sync_configs");

            migrationBuilder.DropTable(
                name: "compliance_form_submissions");

            migrationBuilder.DropTable(
                name: "configurator_options");

            migrationBuilder.DropTable(
                name: "consignment_transactions");

            migrationBuilder.DropTable(
                name: "contact_interactions");

            migrationBuilder.DropTable(
                name: "contact_outreach_preferences");

            migrationBuilder.DropTable(
                name: "cost_calculation_inputs");

            migrationBuilder.DropTable(
                name: "credit_holds");

            migrationBuilder.DropTable(
                name: "customer_portal_accesses");

            migrationBuilder.DropTable(
                name: "customer_returns");

            migrationBuilder.DropTable(
                name: "customer_segments");

            migrationBuilder.DropTable(
                name: "cycle_count_lines");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "deliverables");

            migrationBuilder.DropTable(
                name: "discovery_runs");

            migrationBuilder.DropTable(
                name: "document_embeddings");

            migrationBuilder.DropTable(
                name: "document_revisions");

            migrationBuilder.DropTable(
                name: "domain_event_failures");

            migrationBuilder.DropTable(
                name: "downtime_logs");

            migrationBuilder.DropTable(
                name: "eco_affected_items");

            migrationBuilder.DropTable(
                name: "ecommerce_order_syncs");

            migrationBuilder.DropTable(
                name: "edi_mappings");

            migrationBuilder.DropTable(
                name: "edi_transactions");

            migrationBuilder.DropTable(
                name: "employee_profiles");

            migrationBuilder.DropTable(
                name: "entity_capability_requirements");

            migrationBuilder.DropTable(
                name: "entity_cloud_links");

            migrationBuilder.DropTable(
                name: "entity_notes");

            migrationBuilder.DropTable(
                name: "entity_readiness_validators");

            migrationBuilder.DropTable(
                name: "event_attendees");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "fmea_items");

            migrationBuilder.DropTable(
                name: "follow_up_tasks");

            migrationBuilder.DropTable(
                name: "forecast_overrides");

            migrationBuilder.DropTable(
                name: "holidays");

            migrationBuilder.DropTable(
                name: "icp_dimensions");

            migrationBuilder.DropTable(
                name: "identity_documents");

            migrationBuilder.DropTable(
                name: "integration_outbox_entries");

            migrationBuilder.DropTable(
                name: "inter_plant_transfer_lines");

            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "job_activity_logs");

            migrationBuilder.DropTable(
                name: "job_links");

            migrationBuilder.DropTable(
                name: "job_notes");

            migrationBuilder.DropTable(
                name: "job_parts");

            migrationBuilder.DropTable(
                name: "job_subtasks");

            migrationBuilder.DropTable(
                name: "kanban_trigger_logs");

            migrationBuilder.DropTable(
                name: "kiosk_terminals");

            migrationBuilder.DropTable(
                name: "labor_rates");

            migrationBuilder.DropTable(
                name: "lead_outreach_preferences");

            migrationBuilder.DropTable(
                name: "leave_balances");

            migrationBuilder.DropTable(
                name: "leave_requests");

            migrationBuilder.DropTable(
                name: "lot_records");

            migrationBuilder.DropTable(
                name: "machine_data_points");

            migrationBuilder.DropTable(
                name: "maintenance_logs");

            migrationBuilder.DropTable(
                name: "master_schedule_lines");

            migrationBuilder.DropTable(
                name: "material_issues");

            migrationBuilder.DropTable(
                name: "mfa_recovery_codes");

            migrationBuilder.DropTable(
                name: "ml_models");

            migrationBuilder.DropTable(
                name: "mrp_demands");

            migrationBuilder.DropTable(
                name: "mrp_exceptions");

            migrationBuilder.DropTable(
                name: "mrp_supplies");

            migrationBuilder.DropTable(
                name: "non_conformances");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "oauth_state_tokens");

            migrationBuilder.DropTable(
                name: "operation_materials");

            migrationBuilder.DropTable(
                name: "overtime_rules");

            migrationBuilder.DropTable(
                name: "part_alternates");

            migrationBuilder.DropTable(
                name: "part_prices");

            migrationBuilder.DropTable(
                name: "pay_stub_deductions");

            migrationBuilder.DropTable(
                name: "payment_applications");

            migrationBuilder.DropTable(
                name: "payment_batch_items");

            migrationBuilder.DropTable(
                name: "payment_transmissions");

            migrationBuilder.DropTable(
                name: "performance_reviews");

            migrationBuilder.DropTable(
                name: "pick_lines");

            migrationBuilder.DropTable(
                name: "planning_cycle_entries");

            migrationBuilder.DropTable(
                name: "ppap_elements");

            migrationBuilder.DropTable(
                name: "prediction_feedbacks");

            migrationBuilder.DropTable(
                name: "price_list_entries");

            migrationBuilder.DropTable(
                name: "product_configurations");

            migrationBuilder.DropTable(
                name: "purchase_order_releases");

            migrationBuilder.DropTable(
                name: "qc_inspection_results");

            migrationBuilder.DropTable(
                name: "quote_lines");

            migrationBuilder.DropTable(
                name: "receiving_inspections");

            migrationBuilder.DropTable(
                name: "recurring_expenses");

            migrationBuilder.DropTable(
                name: "recurring_order_lines");

            migrationBuilder.DropTable(
                name: "reorder_suggestions");

            migrationBuilder.DropTable(
                name: "report_schedules");

            migrationBuilder.DropTable(
                name: "reservations");

            migrationBuilder.DropTable(
                name: "rfq_vendor_responses");

            migrationBuilder.DropTable(
                name: "sample_shipments");

            migrationBuilder.DropTable(
                name: "schedule_milestones");

            migrationBuilder.DropTable(
                name: "scheduled_operations");

            migrationBuilder.DropTable(
                name: "scheduled_tasks");

            migrationBuilder.DropTable(
                name: "serial_histories");

            migrationBuilder.DropTable(
                name: "shift_assignments");

            migrationBuilder.DropTable(
                name: "shipment_packages");

            migrationBuilder.DropTable(
                name: "spc_control_limits");

            migrationBuilder.DropTable(
                name: "spc_ooc_events");

            migrationBuilder.DropTable(
                name: "status_entries");

            migrationBuilder.DropTable(
                name: "subcontract_orders");

            migrationBuilder.DropTable(
                name: "supported_languages");

            migrationBuilder.DropTable(
                name: "sync_queue_entries");

            migrationBuilder.DropTable(
                name: "system_api_keys");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "tariff_rates");

            migrationBuilder.DropTable(
                name: "tax_documents");

            migrationBuilder.DropTable(
                name: "terminology_entries");

            migrationBuilder.DropTable(
                name: "time_correction_logs");

            migrationBuilder.DropTable(
                name: "training_path_enrollments");

            migrationBuilder.DropTable(
                name: "training_path_modules");

            migrationBuilder.DropTable(
                name: "training_progress");

            migrationBuilder.DropTable(
                name: "training_scan_logs");

            migrationBuilder.DropTable(
                name: "translated_labels");

            migrationBuilder.DropTable(
                name: "uom_conversions");

            migrationBuilder.DropTable(
                name: "user_cloud_storage_links");

            migrationBuilder.DropTable(
                name: "user_integrations");

            migrationBuilder.DropTable(
                name: "user_mfa_devices");

            migrationBuilder.DropTable(
                name: "user_preferences");

            migrationBuilder.DropTable(
                name: "user_scan_devices");

            migrationBuilder.DropTable(
                name: "user_scan_identifiers");

            migrationBuilder.DropTable(
                name: "vendor_bill_lines");

            migrationBuilder.DropTable(
                name: "vendor_part_price_tiers");

            migrationBuilder.DropTable(
                name: "vendor_payment_applications");

            migrationBuilder.DropTable(
                name: "vendor_scorecards");

            migrationBuilder.DropTable(
                name: "wbs_cost_entries");

            migrationBuilder.DropTable(
                name: "webhook_deliveries");

            migrationBuilder.DropTable(
                name: "work_center_calendars");

            migrationBuilder.DropTable(
                name: "work_center_qualifications");

            migrationBuilder.DropTable(
                name: "work_center_shifts");

            migrationBuilder.DropTable(
                name: "workflow_definitions");

            migrationBuilder.DropTable(
                name: "workflow_run_entities");

            migrationBuilder.DropTable(
                name: "abc_classification_runs");

            migrationBuilder.DropTable(
                name: "acct_bank_reconciliations");

            migrationBuilder.DropTable(
                name: "acct_bank_statement_imports");

            migrationBuilder.DropTable(
                name: "acct_journal_lines");

            migrationBuilder.DropTable(
                name: "acct_fixed_assets");

            migrationBuilder.DropTable(
                name: "acct_journal_templates");

            migrationBuilder.DropTable(
                name: "acct_pay_runs");

            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "approval_requests");

            migrationBuilder.DropTable(
                name: "asp_net_roles");

            migrationBuilder.DropTable(
                name: "scan_action_logs");

            migrationBuilder.DropTable(
                name: "gages");

            migrationBuilder.DropTable(
                name: "capabilities");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "form_definition_versions");

            migrationBuilder.DropTable(
                name: "consignment_agreements");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "cycle_counts");

            migrationBuilder.DropTable(
                name: "controlled_documents");

            migrationBuilder.DropTable(
                name: "engineering_change_orders");

            migrationBuilder.DropTable(
                name: "ecommerce_integrations");

            migrationBuilder.DropTable(
                name: "edi_trading_partners");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "fmea_analyses");

            migrationBuilder.DropTable(
                name: "demand_forecasts");

            migrationBuilder.DropTable(
                name: "icp_rubrics");

            migrationBuilder.DropTable(
                name: "inter_plant_transfers");

            migrationBuilder.DropTable(
                name: "kanban_cards");

            migrationBuilder.DropTable(
                name: "leave_policies");

            migrationBuilder.DropTable(
                name: "machine_tags");

            migrationBuilder.DropTable(
                name: "maintenance_schedules");

            migrationBuilder.DropTable(
                name: "corrective_actions");

            migrationBuilder.DropTable(
                name: "bomlines");

            migrationBuilder.DropTable(
                name: "pay_stubs");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "vendor_bank_accounts");

            migrationBuilder.DropTable(
                name: "payment_batches");

            migrationBuilder.DropTable(
                name: "review_cycles");

            migrationBuilder.DropTable(
                name: "pick_waves");

            migrationBuilder.DropTable(
                name: "shipment_lines");

            migrationBuilder.DropTable(
                name: "planning_cycles");

            migrationBuilder.DropTable(
                name: "maintenance_predictions");

            migrationBuilder.DropTable(
                name: "price_lists");

            migrationBuilder.DropTable(
                name: "product_configurators");

            migrationBuilder.DropTable(
                name: "qc_checklist_items");

            migrationBuilder.DropTable(
                name: "receiving_records");

            migrationBuilder.DropTable(
                name: "qc_inspections");

            migrationBuilder.DropTable(
                name: "recurring_orders");

            migrationBuilder.DropTable(
                name: "saved_reports");

            migrationBuilder.DropTable(
                name: "bin_contents");

            migrationBuilder.DropTable(
                name: "request_for_quotes");

            migrationBuilder.DropTable(
                name: "leads");

            migrationBuilder.DropTable(
                name: "schedule_runs");

            migrationBuilder.DropTable(
                name: "serial_numbers");

            migrationBuilder.DropTable(
                name: "spc_measurements");

            migrationBuilder.DropTable(
                name: "time_entries");

            migrationBuilder.DropTable(
                name: "training_paths");

            migrationBuilder.DropTable(
                name: "training_modules");

            migrationBuilder.DropTable(
                name: "cloud_storage_providers");

            migrationBuilder.DropTable(
                name: "vendor_parts");

            migrationBuilder.DropTable(
                name: "vendor_bills");

            migrationBuilder.DropTable(
                name: "vendor_payments");

            migrationBuilder.DropTable(
                name: "wbs_elements");

            migrationBuilder.DropTable(
                name: "webhook_subscriptions");

            migrationBuilder.DropTable(
                name: "shifts");

            migrationBuilder.DropTable(
                name: "workflow_runs");

            migrationBuilder.DropTable(
                name: "acct_gl_accounts");

            migrationBuilder.DropTable(
                name: "acct_cost_centers");

            migrationBuilder.DropTable(
                name: "acct_journal_entries");

            migrationBuilder.DropTable(
                name: "announcement_templates");

            migrationBuilder.DropTable(
                name: "approval_workflows");

            migrationBuilder.DropTable(
                name: "chat_rooms");

            migrationBuilder.DropTable(
                name: "compliance_form_templates");

            migrationBuilder.DropTable(
                name: "ppap_submissions");

            migrationBuilder.DropTable(
                name: "master_schedules");

            migrationBuilder.DropTable(
                name: "plants");

            migrationBuilder.DropTable(
                name: "machine_connections");

            migrationBuilder.DropTable(
                name: "shipments");

            migrationBuilder.DropTable(
                name: "purchase_order_lines");

            migrationBuilder.DropTable(
                name: "production_runs");

            migrationBuilder.DropTable(
                name: "qc_checklist_templates");

            migrationBuilder.DropTable(
                name: "lead_sources");

            migrationBuilder.DropTable(
                name: "outreach_campaigns");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "spc_characteristics");

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "acct_fiscal_periods");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "part_purchase_units");

            migrationBuilder.DropTable(
                name: "operations");

            migrationBuilder.DropTable(
                name: "acct_fiscal_years");

            migrationBuilder.DropTable(
                name: "acct_books");

            migrationBuilder.DropTable(
                name: "currencies");

            migrationBuilder.DropTable(
                name: "parts");

            migrationBuilder.DropTable(
                name: "reference_data");

            migrationBuilder.DropTable(
                name: "storage_locations");

            migrationBuilder.DropTable(
                name: "cost_calculations");

            migrationBuilder.DropTable(
                name: "costing_profiles");

            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "work_centers");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropTable(
                name: "job_stages");

            migrationBuilder.DropTable(
                name: "mrp_planned_orders");

            migrationBuilder.DropTable(
                name: "sales_order_lines");

            migrationBuilder.DropTable(
                name: "bom_revisions");

            migrationBuilder.DropTable(
                name: "file_attachments");

            migrationBuilder.DropTable(
                name: "track_types");

            migrationBuilder.DropTable(
                name: "mrp_runs");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "units_of_measure");

            migrationBuilder.DropTable(
                name: "sales_orders");

            migrationBuilder.DropTable(
                name: "part_revisions");

            migrationBuilder.DropTable(
                name: "vendors");

            migrationBuilder.DropTable(
                name: "quotes");

            migrationBuilder.DropTable(
                name: "asp_net_users");

            migrationBuilder.DropTable(
                name: "customer_addresses");

            migrationBuilder.DropTable(
                name: "company_locations");

            migrationBuilder.DropTable(
                name: "role_templates");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "working_calendars");

            migrationBuilder.DropTable(
                name: "sales_tax_rates");
        }
    }
}
