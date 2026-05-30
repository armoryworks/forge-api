using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditableToSystemSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Promote SystemSetting from BaseEntity → BaseAuditableEntity so
            // every install-wide setting row carries created / updated /
            // deleted timestamps + soft-delete semantics. The Integrations
            // registry now surfaces these for SystemSetting-backed rows
            // (notably the QuickBooks OAuth token) so admins can see when a
            // connection was first established and last refreshed.
            //
            // Existing rows (company.*, qb_oauth_token if present, etc.)
            // backfill with NOW() — there's no truthful historical timestamp
            // to recover, so the install-upgrade moment is the honest pick.
            // We drop the DB-level default afterwards so new rows go through
            // the SetTimestamps() pipeline like every other auditable entity.
            migrationBuilder.Sql(
                "ALTER TABLE system_settings ADD COLUMN created_at timestamp with time zone NOT NULL DEFAULT NOW();");
            migrationBuilder.Sql(
                "ALTER TABLE system_settings ALTER COLUMN created_at DROP DEFAULT;");

            migrationBuilder.Sql(
                "ALTER TABLE system_settings ADD COLUMN updated_at timestamp with time zone NOT NULL DEFAULT NOW();");
            migrationBuilder.Sql(
                "ALTER TABLE system_settings ALTER COLUMN updated_at DROP DEFAULT;");

            migrationBuilder.AddColumn<System.DateTimeOffset>(
                name: "deleted_at",
                table: "system_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deleted_by",
                table: "system_settings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "system_settings");
        }
    }
}
