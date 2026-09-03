using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Memory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "memory");

            migrationBuilder.RenameTable(
                name: "memory_facts",
                newName: "memory_facts",
                newSchema: "memory");

            migrationBuilder.RenameTable(
                name: "learning_candidates",
                newName: "learning_candidates",
                newSchema: "memory");

            migrationBuilder.RenameTable(
                name: "chat_messages",
                newName: "chat_messages",
                newSchema: "memory");

            migrationBuilder.RenameTable(
                name: "chat_checkpoints",
                newName: "chat_checkpoints",
                newSchema: "memory");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "memory_facts",
                schema: "memory",
                newName: "memory_facts");

            migrationBuilder.RenameTable(
                name: "learning_candidates",
                schema: "memory",
                newName: "learning_candidates");

            migrationBuilder.RenameTable(
                name: "chat_messages",
                schema: "memory",
                newName: "chat_messages");

            migrationBuilder.RenameTable(
                name: "chat_checkpoints",
                schema: "memory",
                newName: "chat_checkpoints");
        }
    }
}
