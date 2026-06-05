using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorBillApSubledger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vendor_bills",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    bill_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vendor_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_invoice_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    purchase_order_id = table.Column<int>(type: "integer", nullable: true),
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
                name: "vendor_bill_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_bill_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
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
                        name: "fk_vendor_bill_lines_part",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "vendor_payment_applications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_payment_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_bill_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bill_lines_part",
                table: "vendor_bill_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_bill_lines_vendor_bill_id",
                table: "vendor_bill_lines",
                column: "vendor_bill_id");

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
                name: "ux_vendor_bills_number",
                table: "vendor_bills",
                column: "bill_number",
                unique: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vendor_bill_lines");

            migrationBuilder.DropTable(
                name: "vendor_payment_applications");

            migrationBuilder.DropTable(
                name: "vendor_bills");

            migrationBuilder.DropTable(
                name: "vendor_payments");
        }
    }
}
