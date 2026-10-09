using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_AddPatientChangeSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "patient_change_summary",
                table: "nutrition_plans",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "patient_message",
                table: "nutrition_plans",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "patient_change_summary",
                table: "nutrition_plans");

            migrationBuilder.DropColumn(
                name: "patient_message",
                table: "nutrition_plans");
        }
    }
}
