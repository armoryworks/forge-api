using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOeeSubcontractInspectionUomApprovalCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_holds");

            migrationBuilder.DropTable(
                name: "receiving_inspections");
        }
    }
}
