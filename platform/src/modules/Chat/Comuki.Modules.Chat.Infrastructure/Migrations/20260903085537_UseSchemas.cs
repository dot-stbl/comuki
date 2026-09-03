using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "chat");

            migrationBuilder.RenameTable(
                name: "chat_sessions",
                newName: "chat_sessions",
                newSchema: "chat");

            migrationBuilder.RenameTable(
                name: "chat_messages",
                newName: "chat_messages",
                newSchema: "chat");

            migrationBuilder.RenameTable(
                name: "chat_checkpoints",
                newName: "chat_checkpoints",
                newSchema: "chat");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "chat_sessions",
                schema: "chat",
                newName: "chat_sessions");

            migrationBuilder.RenameTable(
                name: "chat_messages",
                schema: "chat",
                newName: "chat_messages");

            migrationBuilder.RenameTable(
                name: "chat_checkpoints",
                schema: "chat",
                newName: "chat_checkpoints");
        }
    }
}
