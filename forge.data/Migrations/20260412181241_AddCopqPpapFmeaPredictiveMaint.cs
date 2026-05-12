using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCopqPpapFmeaPredictiveMaint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fmea_items");

            migrationBuilder.DropTable(
                name: "ml_models");

            migrationBuilder.DropTable(
                name: "ppap_elements");

            migrationBuilder.DropTable(
                name: "prediction_feedbacks");

            migrationBuilder.DropTable(
                name: "fmea_analyses");

            migrationBuilder.DropTable(
                name: "maintenance_predictions");

            migrationBuilder.DropTable(
                name: "ppap_submissions");
        }
    }
}
