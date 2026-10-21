using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class IntakeBodyResponse_AddDiaryEntryPatientTimestampIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_diary_entries_patient_id_local_timestamp",
                table: "diary_entries",
                columns: new[] { "patient_id", "local_timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_diary_entries_patient_id_local_timestamp",
                table: "diary_entries");
        }
    }
}
