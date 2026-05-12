using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Pillar3_AddVendorPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vendor_parts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_part_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vendor_mpn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    min_order_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    pack_size = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    country_of_origin = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    hts_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_approved = table.Column<bool>(type: "boolean", nullable: false),
                    is_preferred = table.Column<bool>(type: "boolean", nullable: false),
                    certifications = table.Column<string>(type: "jsonb", nullable: true),
                    last_quoted_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                name: "vendor_part_price_tiers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_part_id = table.Column<int>(type: "integer", nullable: false),
                    min_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_part_price_tiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_part_price_tiers_vendor_parts_vendor_part_id",
                        column: x => x.vendor_part_id,
                        principalTable: "vendor_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            // Pillar 3 backfill — for every Part with a PreferredVendorId,
            // create a VendorPart row with the per-vendor sourcing snapshot
            // copied from the Part row. Marked IsPreferred so the existing
            // "preferred vendor" semantics carry over. Part.MinOrderQty,
            // Part.PackSize, Part.LeadTimeDays are kept on Part as the
            // current snapshot — Phase 2/4 work will migrate readers off
            // them once the new VendorPart surfaces are in place.
            migrationBuilder.Sql("""
                INSERT INTO vendor_parts (
                    vendor_id, part_id,
                    min_order_qty, pack_size, lead_time_days,
                    is_approved, is_preferred,
                    created_at, updated_at
                )
                SELECT
                    p.preferred_vendor_id, p.id,
                    p.min_order_qty, p.pack_size, p.lead_time_days,
                    true, true,
                    NOW() AT TIME ZONE 'UTC', NOW() AT TIME ZONE 'UTC'
                FROM parts p
                WHERE p.preferred_vendor_id IS NOT NULL
                  AND p.deleted_at IS NULL
                ON CONFLICT (vendor_id, part_id) DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vendor_part_price_tiers");

            migrationBuilder.DropTable(
                name: "vendor_parts");
        }
    }
}
