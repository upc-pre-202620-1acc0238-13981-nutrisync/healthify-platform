using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_StructuredAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "physical_activity",
                table: "nutritional_assessments",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "medical_history",
                table: "nutritional_assessments",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<string>(
                name: "habits",
                table: "nutritional_assessments",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AddColumn<string>(
                name: "activity_level",
                table: "nutritional_assessments",
                type: "varchar(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "conditions_snapshot",
                table: "nutritional_assessments",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "consultation_id",
                table: "nutritional_assessments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "glucose_mg_dl",
                table: "nutritional_assessments",
                type: "decimal(6,1)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "meals_out_per_week",
                table: "nutritional_assessments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "meals_per_day",
                table: "nutritional_assessments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_cholesterol_mg_dl",
                table: "nutritional_assessments",
                type: "decimal(6,1)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "triglycerides_mg_dl",
                table: "nutritional_assessments",
                type: "decimal(6,1)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "water_liters_per_day",
                table: "nutritional_assessments",
                type: "decimal(4,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "bmi",
                table: "clinical_measurements",
                type: "decimal(5,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "bmi_category",
                table: "clinical_measurements",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "protocol_checks",
                table: "clinical_measurements",
                type: "json",
                nullable: true);

            // Backfill (NC-3): every existing measurement has weight and height, so its body mass index
            // and WHO category can be computed. Exact DECIMAL arithmetic (weight * 10000 / height²) so
            // ROUND(.., 1) rounds half away from zero, as BodyMassIndex does; the category is taken from
            // the rounded value.
            migrationBuilder.Sql(@"
UPDATE clinical_measurements
SET bmi = ROUND(CAST(weight_kg * 10000 AS DECIMAL(30,12)) / (height_cm * height_cm), 1);");
            migrationBuilder.Sql(@"
UPDATE clinical_measurements
SET bmi_category = CASE
    WHEN bmi < 18.5 THEN 'Underweight'
    WHEN bmi < 25 THEN 'NormalWeight'
    WHEN bmi < 30 THEN 'OverweightGradeI'
    WHEN bmi < 35 THEN 'ObesityGradeI'
    WHEN bmi < 40 THEN 'ObesityGradeII'
    ELSE 'ObesityGradeIII'
END;");

            migrationBuilder.CreateIndex(
                name: "ix_nutritional_assessments_consultation_id",
                table: "nutritional_assessments",
                column: "consultation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nutritional_assessments_consultation_id",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "activity_level",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "conditions_snapshot",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "consultation_id",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "glucose_mg_dl",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "meals_out_per_week",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "meals_per_day",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "total_cholesterol_mg_dl",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "triglycerides_mg_dl",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "water_liters_per_day",
                table: "nutritional_assessments");

            migrationBuilder.DropColumn(
                name: "bmi",
                table: "clinical_measurements");

            migrationBuilder.DropColumn(
                name: "bmi_category",
                table: "clinical_measurements");

            migrationBuilder.DropColumn(
                name: "protocol_checks",
                table: "clinical_measurements");

            // Rows recorded through the guided consultation have no free text; give them an empty one
            // so the columns can become NOT NULL again.
            migrationBuilder.Sql(
                "UPDATE nutritional_assessments SET physical_activity = COALESCE(physical_activity, ''), " +
                "medical_history = COALESCE(medical_history, ''), habits = COALESCE(habits, '');");

            migrationBuilder.AlterColumn<string>(
                name: "physical_activity",
                table: "nutritional_assessments",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "medical_history",
                table: "nutritional_assessments",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "habits",
                table: "nutritional_assessments",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
