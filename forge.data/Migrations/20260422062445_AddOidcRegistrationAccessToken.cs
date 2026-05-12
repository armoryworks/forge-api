using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOidcRegistrationAccessToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "registration_access_token_hash",
                table: "oidc_client_metadata",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "registration_access_token_rotated_at",
                table: "oidc_client_metadata",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "registration_access_token_hash",
                table: "oidc_client_metadata");

            migrationBuilder.DropColumn(
                name: "registration_access_token_rotated_at",
                table: "oidc_client_metadata");
        }
    }
}
