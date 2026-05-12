using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSerialNumberTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_serial_tracked",
                table: "parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "serial_histories");

            migrationBuilder.DropTable(
                name: "serial_numbers");

            migrationBuilder.DropColumn(
                name: "is_serial_tracked",
                table: "parts");
        }
    }
}
