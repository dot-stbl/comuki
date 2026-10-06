using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWikiSourceKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "link_graph",
                schema: "knowledge",
                table: "source_documents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_mission_id",
                schema: "knowledge",
                table: "source_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "wiki_page_id",
                schema: "knowledge",
                table: "source_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "wiki_page_kind",
                schema: "knowledge",
                table: "source_documents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_documents_wiki_page_id",
                schema: "knowledge",
                table: "source_documents",
                column: "wiki_page_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_source_documents_wiki_page_id",
                schema: "knowledge",
                table: "source_documents");

            migrationBuilder.DropColumn(
                name: "link_graph",
                schema: "knowledge",
                table: "source_documents");

            migrationBuilder.DropColumn(
                name: "updated_by_mission_id",
                schema: "knowledge",
                table: "source_documents");

            migrationBuilder.DropColumn(
                name: "wiki_page_id",
                schema: "knowledge",
                table: "source_documents");

            migrationBuilder.DropColumn(
                name: "wiki_page_kind",
                schema: "knowledge",
                table: "source_documents");
        }
    }
}
