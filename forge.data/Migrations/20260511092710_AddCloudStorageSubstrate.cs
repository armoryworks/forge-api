using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudStorageSubstrate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cloud_storage_providers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    provider_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    root_folder_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    service_account_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    oauth_token_encrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    refresh_token_encrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    settings = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cloud_storage_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entity_cloud_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    provider_id = table.Column<int>(type: "integer", nullable: false),
                    folder_external_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    folder_path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    folder_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_via = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_cloud_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_cloud_links_cloud_storage_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "cloud_storage_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_cloud_storage_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<int>(type: "integer", nullable: false),
                    external_user_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    oauth_token_encrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    refresh_token_encrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_cloud_storage_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_cloud_storage_links_cloud_storage_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "cloud_storage_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cloud_storage_providers_provider_code_is_active",
                table: "cloud_storage_providers",
                columns: new[] { "provider_code", "is_active" },
                unique: true,
                filter: "is_active = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entity_cloud_links_entity_type_entity_id",
                table: "entity_cloud_links",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_entity_cloud_links_entity_type_entity_id_provider_id",
                table: "entity_cloud_links",
                columns: new[] { "entity_type", "entity_id", "provider_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entity_cloud_links_provider_id",
                table: "entity_cloud_links",
                column: "provider_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_cloud_storage_links_provider_id",
                table: "user_cloud_storage_links",
                column: "provider_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_cloud_storage_links_user_id_provider_id",
                table: "user_cloud_storage_links",
                columns: new[] { "user_id", "provider_id" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_cloud_links");

            migrationBuilder.DropTable(
                name: "user_cloud_storage_links");

            migrationBuilder.DropTable(
                name: "cloud_storage_providers");
        }
    }
}
