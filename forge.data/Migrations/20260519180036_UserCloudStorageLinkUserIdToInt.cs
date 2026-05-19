using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserCloudStorageLinkUserIdToInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The original user_cloud_storage_links.user_id was uuid — an
            // Artifact 4 / D3 mistake that never matched ApplicationUser.Id
            // (which is int). No code ever wrote rows against this table
            // (verified by grep for `.Add(new UserCloudStorageLink)`); the
            // pre-Phase-2c entity + migration shipped May 11 with zero
            // writers since. So this migration drops + re-adds the column
            // rather than ALTER COLUMN TYPE (which would require a USING
            // clause on Postgres and would be lossy on a populated table —
            // the right safety net even with current empty-table reality).

            migrationBuilder.DropIndex(
                name: "ix_user_cloud_storage_links_user_id_provider_id",
                table: "user_cloud_storage_links");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "user_cloud_storage_links");

            migrationBuilder.AddColumn<int>(
                name: "user_id",
                table: "user_cloud_storage_links",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_user_cloud_storage_links_user_id_provider_id",
                table: "user_cloud_storage_links",
                columns: new[] { "user_id", "provider_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_user_cloud_storage_links__asp_net_users_user_id",
                table: "user_cloud_storage_links",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_cloud_storage_links__asp_net_users_user_id",
                table: "user_cloud_storage_links");

            migrationBuilder.DropIndex(
                name: "ix_user_cloud_storage_links_user_id_provider_id",
                table: "user_cloud_storage_links");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "user_cloud_storage_links");

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "user_cloud_storage_links",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_user_cloud_storage_links_user_id_provider_id",
                table: "user_cloud_storage_links",
                columns: new[] { "user_id", "provider_id" },
                unique: true,
                filter: "deleted_at IS NULL");
        }
    }
}
