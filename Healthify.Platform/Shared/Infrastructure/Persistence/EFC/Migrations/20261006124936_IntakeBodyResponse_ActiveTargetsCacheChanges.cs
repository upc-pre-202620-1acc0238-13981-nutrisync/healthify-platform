using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class IntakeBodyResponse_ActiveTargetsCacheChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "changes_from_previous",
                table: "active_targets_caches",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "patient_message",
                table: "active_targets_caches",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "changes_from_previous",
                table: "active_targets_caches");

            migrationBuilder.DropColumn(
                name: "patient_message",
                table: "active_targets_caches");
        }
    }
}
