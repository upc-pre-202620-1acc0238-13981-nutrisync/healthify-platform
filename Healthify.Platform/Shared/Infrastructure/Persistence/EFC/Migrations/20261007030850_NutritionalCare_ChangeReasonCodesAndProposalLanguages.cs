using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_ChangeReasonCodesAndProposalLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "patient_language",
                table: "review_item_plan_proposals",
                type: "varchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "practitioner_language",
                table: "review_item_plan_proposals",
                type: "varchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "change_reason_code",
                table: "nutrition_plans",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "change_reason_data",
                table: "nutrition_plans",
                type: "json",
                nullable: true);

            // X-2. Backfill: the two sentences the system wrote before X-2 become their code and date; every other
            // reason was written by a practitioner and is Custom. The sentence stays as it was (legacy fallback).
            // The months are the ones ChangeReason spells ("18 sept. 2026"); "[.]" and "." stand for the dot and the
            // accented letters, so the match does not depend on escaping or on the collation.
            const string datePattern = "[0-9]{1,2} (ene|feb|mar|abr|may|jun|jul|ago|sept|oct|nov|dic)[.] [0-9]{4}$";
            const string isoDate =
                "CONCAT(SUBSTRING_INDEX(change_reason, ' ', -1), '-', " +
                "LPAD(FIELD(SUBSTRING_INDEX(SUBSTRING_INDEX(change_reason, ' ', -2), ' ', 1), " +
                "'ene.', 'feb.', 'mar.', 'abr.', 'may.', 'jun.', 'jul.', 'ago.', 'sept.', 'oct.', 'nov.', 'dic.'), 2, '0'), '-', " +
                "LPAD(SUBSTRING_INDEX(SUBSTRING_INDEX(change_reason, ' ', -3), ' ', 1), 2, '0'))";

            migrationBuilder.Sql(
                "UPDATE nutrition_plans " +
                "SET change_reason_code = 'NewConsultation', " +
                $"change_reason_data = JSON_OBJECT('date', {isoDate}) " +
                "WHERE change_reason IS NOT NULL " +
                $"AND REGEXP_LIKE(change_reason, '^Nueva consulta del {datePattern}', 'c');");

            migrationBuilder.Sql(
                "UPDATE nutrition_plans " +
                "SET change_reason_code = 'SignalAdjustment', " +
                $"change_reason_data = JSON_OBJECT('date', {isoDate}, 'signalType', 'SustainedDeviation') " +
                "WHERE change_reason IS NOT NULL AND change_reason_code IS NULL " +
                $"AND REGEXP_LIKE(change_reason, '^Ajuste por se.al: desviaci.n sostenida del {datePattern}', 'c');");

            migrationBuilder.Sql(
                "UPDATE nutrition_plans SET change_reason_code = 'Custom' " +
                "WHERE change_reason IS NOT NULL AND change_reason_code IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "patient_language",
                table: "review_item_plan_proposals");

            migrationBuilder.DropColumn(
                name: "practitioner_language",
                table: "review_item_plan_proposals");

            migrationBuilder.DropColumn(
                name: "change_reason_code",
                table: "nutrition_plans");

            migrationBuilder.DropColumn(
                name: "change_reason_data",
                table: "nutrition_plans");
        }
    }
}
