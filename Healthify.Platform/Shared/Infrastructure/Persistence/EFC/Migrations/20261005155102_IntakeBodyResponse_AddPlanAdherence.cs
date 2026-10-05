using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class IntakeBodyResponse_AddPlanAdherence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "plan_adherence",
                table: "diary_entries",
                type: "varchar(15)",
                maxLength: 15,
                nullable: false,
                defaultValueSql: "'NotAnswered'");

            // IN-1 backfill: the legacy one-tap off-plan entries keep reading as «Fuera del plan».
            migrationBuilder.Sql(
                "UPDATE diary_entries SET plan_adherence = 'OffPlan' WHERE provenance = 'OffPlan';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "plan_adherence",
                table: "diary_entries");
        }
    }
}
