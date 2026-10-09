using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringAdherence_ConsistencyPromptAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "prompt_issued_at",
                table: "consistency_indices",
                type: "datetime",
                nullable: true);

            // MA-7. Before this migration the prompt date was written when the prompt was issued, so for existing
            // rows both moments are that date. shown_to_patient_at is not rewritten: those prompts keep counting as
            // shown, as they did (DECISIÓN MA-7).
            migrationBuilder.Sql(
                "UPDATE consistency_indices SET prompt_issued_at = shown_to_patient_at WHERE shown_to_patient_at IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "prompt_issued_at",
                table: "consistency_indices");
        }
    }
}
