using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_ReviewItemResolutionNoteCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "resolution_note_code",
                table: "review_items",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_note_data",
                table: "review_items",
                type: "json",
                nullable: true);

            // X-2. Backfill: the two sentences the system wrote when a practitioner assigned an AI proposal become their
            // code and plan version; every other note was written by a practitioner and is Custom; a resolved item
            // without a note whose proposal was dismissed is ProposalDiscarded. The sentence stays as it was (legacy
            // fallback). "[(]" and "[)]" stand for the parentheses, so the match does not depend on escaping.
            const string planVersion = "JSON_OBJECT('planVersion', CAST(REGEXP_SUBSTR(resolution_note, '[0-9]+') AS UNSIGNED))";

            migrationBuilder.Sql(
                "UPDATE review_items " +
                $"SET resolution_note_code = 'PlanAssignedAsIs', resolution_note_data = {planVersion} " +
                "WHERE resolution_note IS NOT NULL " +
                "AND REGEXP_LIKE(resolution_note, '^Plan v[0-9]+ asignado [(]propuesta IA aceptada tal cual[)]$', 'c');");

            migrationBuilder.Sql(
                "UPDATE review_items " +
                $"SET resolution_note_code = 'PlanAssignedWithEdits', resolution_note_data = {planVersion} " +
                "WHERE resolution_note IS NOT NULL AND resolution_note_code IS NULL " +
                "AND REGEXP_LIKE(resolution_note, '^Plan v[0-9]+ asignado [(]propuesta IA aceptada con ediciones[)]$', 'c');");

            migrationBuilder.Sql(
                "UPDATE review_items SET resolution_note_code = 'Custom' " +
                "WHERE resolution_note IS NOT NULL AND resolution_note_code IS NULL;");

            migrationBuilder.Sql(
                "UPDATE review_items r " +
                "JOIN review_item_plan_proposals p ON p.review_item_id = r.id AND p.status = 'Dismissed' " +
                "SET r.resolution_note_code = 'ProposalDiscarded' " +
                "WHERE r.state = 'Resolved' AND r.resolution_note IS NULL AND r.resolution_note_code IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "resolution_note_code",
                table: "review_items");

            migrationBuilder.DropColumn(
                name: "resolution_note_data",
                table: "review_items");
        }
    }
}
