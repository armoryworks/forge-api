using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThreadsAndMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "parent_message_id",
                table: "chat_messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "thread_last_reply_at",
                table: "chat_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "thread_reply_count",
                table: "chat_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "chat_message_mentions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_message_id = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    display_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_message_mentions", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_message_mentions_chat_messages_chat_message_id",
                        column: x => x.chat_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_parent_message_id",
                table: "chat_messages",
                column: "parent_message_id",
                filter: "parent_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_chat_message_mentions_chat_message_id",
                table: "chat_message_mentions",
                column: "chat_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_message_mentions_entity_type_entity_id",
                table: "chat_message_mentions",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_chat_messages_chat_messages_parent_message_id",
                table: "chat_messages",
                column: "parent_message_id",
                principalTable: "chat_messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_messages_chat_messages_parent_message_id",
                table: "chat_messages");

            migrationBuilder.DropTable(
                name: "chat_message_mentions");

            migrationBuilder.DropIndex(
                name: "ix_chat_messages_parent_message_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "parent_message_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "thread_last_reply_at",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "thread_reply_count",
                table: "chat_messages");
        }
    }
}
