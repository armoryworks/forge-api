using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capa_tasks");

            migrationBuilder.DropTable(
                name: "edi_mappings");

            migrationBuilder.DropTable(
                name: "edi_transactions");

            migrationBuilder.DropTable(
                name: "non_conformances");

            migrationBuilder.DropTable(
                name: "spc_control_limits");

            migrationBuilder.DropTable(
                name: "spc_ooc_events");

            migrationBuilder.DropTable(
                name: "edi_trading_partners");

            migrationBuilder.DropTable(
                name: "corrective_actions");

            migrationBuilder.DropTable(
                name: "spc_measurements");

            migrationBuilder.DropTable(
                name: "spc_characteristics");
        }
    }
}
