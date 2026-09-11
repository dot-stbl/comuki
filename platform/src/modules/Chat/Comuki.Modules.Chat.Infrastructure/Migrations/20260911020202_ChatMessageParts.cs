using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChatMessageParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's AlterColumn cannot emit the PostgreSQL `USING` clause, and
            // there is no implicit integer -> character varying cast: the
            // int-ordinal columns must be rewritten to the enum member names
            // in the same statement that changes the type. The AlterColumn
            // calls below then restate the (already reached) target shape.
            migrationBuilder.Sql(
                """
                ALTER TABLE chat.chat_sessions
                    ALTER COLUMN status TYPE character varying(16)
                    USING CASE status
                        WHEN 0 THEN 'Active'
                        WHEN 1 THEN 'Archived'
                        ELSE 'Active'
                    END;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE chat.chat_messages
                    ALTER COLUMN role TYPE character varying(16)
                    USING CASE role
                        WHEN 0 THEN 'User'
                        WHEN 1 THEN 'Assistant'
                        WHEN 2 THEN 'System'
                        WHEN 3 THEN 'Tool'
                        ELSE 'User'
                    END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "chat",
                table: "chat_sessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "role",
                schema: "chat",
                table: "chat_messages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "meta",
                schema: "chat",
                table: "chat_messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parts",
                schema: "chat",
                table: "chat_messages",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "meta",
                schema: "chat",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "parts",
                schema: "chat",
                table: "chat_messages");

            // Same EF limitation in reverse: character varying -> integer
            // needs an explicit `USING` cast, and the member names have to be
            // mapped back onto their ordinals.
            migrationBuilder.Sql(
                """
                ALTER TABLE chat.chat_sessions
                    ALTER COLUMN status TYPE integer
                    USING CASE status
                        WHEN 'Active' THEN 0
                        WHEN 'Archived' THEN 1
                        ELSE 0
                    END;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE chat.chat_messages
                    ALTER COLUMN role TYPE integer
                    USING CASE role
                        WHEN 'User' THEN 0
                        WHEN 'Assistant' THEN 1
                        WHEN 'System' THEN 2
                        WHEN 'Tool' THEN 3
                        ELSE 0
                    END;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "status",
                schema: "chat",
                table: "chat_sessions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<int>(
                name: "role",
                schema: "chat",
                table: "chat_messages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);
        }
    }
}
