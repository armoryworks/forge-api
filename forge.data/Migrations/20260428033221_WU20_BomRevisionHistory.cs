using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class WU20_BomRevisionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "current_bom_revision_id",
                table: "parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bom_revision_id_at_release",
                table: "jobs",
                type: "integer",
                nullable: true);

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
                    table.ForeignKey(
                        name: "fk_bom_revisions__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bom_revision_entries",
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
                    table.PrimaryKey("pk_bom_revision_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_bom_revision_entries__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bom_revision_entries_bom_revisions_bom_revision_id",
                        column: x => x.bom_revision_id,
                        principalTable: "bom_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parts_current_bom_revision_id",
                table: "parts",
                column: "current_bom_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_bom_revision_id_at_release",
                table: "jobs",
                column: "bom_revision_id_at_release");

            migrationBuilder.CreateIndex(
                name: "ix_bom_revision_entries_bom_revision_id",
                table: "bom_revision_entries",
                column: "bom_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_bom_revision_entries_part_id",
                table: "bom_revision_entries",
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

            migrationBuilder.AddForeignKey(
                name: "fk_jobs_bom_revisions_bom_revision_id_at_release",
                table: "jobs",
                column: "bom_revision_id_at_release",
                principalTable: "bom_revisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_parts_bom_revisions_current_bom_revision_id",
                table: "parts",
                column: "current_bom_revision_id",
                principalTable: "bom_revisions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Phase 3 H4 / WU-20 — backfill rev 1 for every existing part
            // that has BOM entries. We only create a revision for parts
            // with at least one BOMEntry (no point in a "rev 1" for parts
            // that have no BOM at all). Parts can later get their first
            // revision via the normal create-component flow.
            migrationBuilder.Sql(@"
DO $WU20$
DECLARE
    v_now TIMESTAMPTZ := NOW();
    v_part RECORD;
    v_rev_id INT;
BEGIN
    FOR v_part IN
        SELECT DISTINCT parent_part_id AS pid
          FROM bomentries
         WHERE deleted_at IS NULL
    LOOP
        INSERT INTO bom_revisions
            (part_id, revision_number, effective_date, notes,
             created_by_user_id, created_at, updated_at)
        VALUES
            (v_part.pid, 1, v_now, 'Backfilled from existing BOM (WU-20)',
             NULL, v_now, v_now)
        RETURNING id INTO v_rev_id;

        INSERT INTO bom_revision_entries
            (bom_revision_id, part_id, quantity, unit_of_measure,
             operation_id, reference_designator, source_type, lead_time_days,
             notes, sort_order, created_at, updated_at)
        SELECT
            v_rev_id,
            be.child_part_id,
            be.quantity,
            COALESCE(uom.name, ''),
            NULL,
            be.reference_designator,
            be.source_type,
            be.lead_time_days,
            be.notes,
            be.sort_order,
            v_now,
            v_now
        FROM bomentries be
        LEFT JOIN units_of_measure uom ON uom.id = be.uom_id
        WHERE be.parent_part_id = v_part.pid
          AND be.deleted_at IS NULL;

        UPDATE parts
           SET current_bom_revision_id = v_rev_id,
               updated_at = v_now
         WHERE id = v_part.pid;
    END LOOP;
END
$WU20$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_jobs_bom_revisions_bom_revision_id_at_release",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "fk_parts_bom_revisions_current_bom_revision_id",
                table: "parts");

            migrationBuilder.DropTable(
                name: "bom_revision_entries");

            migrationBuilder.DropTable(
                name: "bom_revisions");

            migrationBuilder.DropIndex(
                name: "ix_parts_current_bom_revision_id",
                table: "parts");

            migrationBuilder.DropIndex(
                name: "ix_jobs_bom_revision_id_at_release",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "current_bom_revision_id",
                table: "parts");

            migrationBuilder.DropColumn(
                name: "bom_revision_id_at_release",
                table: "jobs");
        }
    }
}
