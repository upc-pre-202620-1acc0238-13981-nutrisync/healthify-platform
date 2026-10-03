using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_DiagnosisCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "rationale",
                table: "nutritional_diagnoses",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<long>(
                name: "ai_generation_id",
                table: "nutritional_diagnoses",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "bmi_at_issue",
                table: "nutritional_diagnoses",
                type: "decimal(4,1)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "nutritional_diagnoses",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "nutritional_diagnoses",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_generation_id",
                table: "nutritional_diagnoses");

            migrationBuilder.DropColumn(
                name: "bmi_at_issue",
                table: "nutritional_diagnoses");

            migrationBuilder.DropColumn(
                name: "code",
                table: "nutritional_diagnoses");

            migrationBuilder.DropColumn(
                name: "source",
                table: "nutritional_diagnoses");

            migrationBuilder.AlterColumn<string>(
                name: "rationale",
                table: "nutritional_diagnoses",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
