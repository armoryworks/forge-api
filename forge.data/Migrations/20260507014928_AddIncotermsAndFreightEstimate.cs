using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncotermsAndFreightEstimate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "working_calendar_id",
                table: "company_locations",
                type: "integer",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_company_locations_working_calendar_id",
                table: "company_locations",
                column: "working_calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_holidays_working_calendar_id_date",
                table: "holidays",
                columns: new[] { "working_calendar_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_holidays_working_calendar_id_observed_date",
                table: "holidays",
                columns: new[] { "working_calendar_id", "observed_date" });

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
                name: "fk_company_locations__working_calendars_working_calendar_id",
                table: "company_locations",
                column: "working_calendar_id",
                principalTable: "working_calendars",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_company_locations__working_calendars_working_calendar_id",
                table: "company_locations");

            migrationBuilder.DropTable(
                name: "holidays");

            migrationBuilder.DropTable(
                name: "working_calendars");

            migrationBuilder.DropIndex(
                name: "ix_company_locations_working_calendar_id",
                table: "company_locations");

            migrationBuilder.DropColumn(
                name: "working_calendar_id",
                table: "company_locations");
        }
    }
}
