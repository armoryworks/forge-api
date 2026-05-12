using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGageCalibrationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calibration_records");

            migrationBuilder.DropTable(
                name: "gages");
        }
    }
}
