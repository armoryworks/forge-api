using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "channel_type",
                table: "chat_rooms",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "created_by_system",
                table: "chat_rooms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "chat_rooms",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icon_name",
                table: "chat_rooms",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_read_only",
                table: "chat_rooms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "team_id",
                table: "chat_rooms",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_read_message_id",
                table: "chat_room_members",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "muted_until",
                table: "chat_room_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "chat_room_members",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_channel_type",
                table: "chat_rooms",
                column: "channel_type");

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_team_id",
                table: "chat_rooms",
                column: "team_id",
                filter: "team_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_chat_room_members_last_read_message_id",
                table: "chat_room_members",
                column: "last_read_message_id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_room_members_chat_messages_last_read_message_id",
                table: "chat_room_members",
                column: "last_read_message_id",
                principalTable: "chat_messages",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_chat_rooms__teams_team_id",
                table: "chat_rooms",
                column: "team_id",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_room_members_chat_messages_last_read_message_id",
                table: "chat_room_members");

            migrationBuilder.DropForeignKey(
                name: "fk_chat_rooms__teams_team_id",
                table: "chat_rooms");

            migrationBuilder.DropIndex(
                name: "ix_chat_rooms_channel_type",
                table: "chat_rooms");

            migrationBuilder.DropIndex(
                name: "ix_chat_rooms_team_id",
                table: "chat_rooms");

            migrationBuilder.DropIndex(
                name: "ix_chat_room_members_last_read_message_id",
                table: "chat_room_members");

            migrationBuilder.DropColumn(
                name: "channel_type",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "created_by_system",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "description",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "icon_name",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "is_read_only",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "team_id",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "last_read_message_id",
                table: "chat_room_members");

            migrationBuilder.DropColumn(
                name: "muted_until",
                table: "chat_room_members");

            migrationBuilder.DropColumn(
                name: "role",
                table: "chat_room_members");
        }
    }
}
