using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class L3_LeadStatusAsString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // L3: leads.status integer → varchar(40). Postgres can't auto-cast int → varchar,
            // so supply an explicit USING. The column now stores the LeadStatus *name*
            // (HasConversion<string>); the pre-beta DB is wiped, so the cast applies to an
            // empty table (no integer codes to translate to names). Raw SQL keeps the cast
            // deterministic across Npgsql versions rather than relying on a scaffolded USING.
            migrationBuilder.Sql(
                "ALTER TABLE leads ALTER COLUMN status TYPE character varying(40) USING status::text;");

            migrationBuilder.CreateIndex(
                name: "ix_leads_status",
                table: "leads",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leads_status",
                table: "leads");

            // Rollback (pre-beta only): enum names can't cast back to int, so reset every
            // row to 0 (LeadStatus.New). Lossy by design — rollback is not a production path.
            migrationBuilder.Sql(
                "ALTER TABLE leads ALTER COLUMN status TYPE integer USING 0;");
        }
    }
}
