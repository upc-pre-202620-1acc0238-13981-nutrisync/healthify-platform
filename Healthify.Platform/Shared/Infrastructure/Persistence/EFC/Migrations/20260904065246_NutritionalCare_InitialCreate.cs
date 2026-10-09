using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nutrition_plans",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    practitioner_id = table.Column<int>(type: "int", nullable: false),
                    diagnosis_id = table.Column<int>(type: "int", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    basis_equation = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    basis_reference_weight_kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    basis_reference_weight_kg = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    basis_activity_factor = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    basis_deficit_kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    basis_deficit_value = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    basis_computed_bmr = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    basis_computed_tdee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    proposal_energy_kcal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    proposal_protein_g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    proposal_carb_g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    proposal_fat_g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    prescribed_energy_kcal = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    prescribed_protein_g = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    prescribed_carb_g = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    prescribed_fat_g = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    prescribed_outcome = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    prescribed_override_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    change_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    guidelines = table.Column<string>(type: "json", nullable: false),
                    restrictions = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_nutrition_plans", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "nutritional_assessments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    practitioner_id = table.Column<int>(type: "int", nullable: false),
                    habits = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    medical_history = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    physical_activity = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    biochemistry = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    age_years = table.Column<int>(type: "int", nullable: false),
                    biological_sex = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    supersedes_assessment_id = table.Column<int>(type: "int", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_nutritional_assessments", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "nutritional_diagnoses",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    practitioner_id = table.Column<int>(type: "int", nullable: false),
                    assessment_id = table.Column<int>(type: "int", nullable: false),
                    statement = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    rationale = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_nutritional_diagnoses", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "review_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    practitioner_id = table.Column<int>(type: "int", nullable: false),
                    signal_type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    evidence = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    resolved_with_adjustment = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    resolution_note = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_review_items", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "clinical_measurements",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    assessment_id = table.Column<int>(type: "int", nullable: false),
                    weight_kg = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    height_cm = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    protocol = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    body_fat_percentage = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    waist_circumference_cm = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    taken_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_clinical_measurements", x => x.id);
                    table.ForeignKey(
                        name: "f_k_clinical_measurements_nutritional_assessments_assessment_id",
                        column: x => x.assessment_id,
                        principalTable: "nutritional_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_measurements_assessment_id",
                table: "clinical_measurements",
                column: "assessment_id");

            migrationBuilder.CreateIndex(
                name: "ix_nutrition_plans_patient_id",
                table: "nutrition_plans",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_nutritional_assessments_patient_id",
                table: "nutritional_assessments",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_nutritional_diagnoses_patient_id",
                table: "nutritional_diagnoses",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_items_patient_id",
                table: "review_items",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_items_practitioner_id",
                table: "review_items",
                column: "practitioner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinical_measurements");

            migrationBuilder.DropTable(
                name: "nutrition_plans");

            migrationBuilder.DropTable(
                name: "nutritional_diagnoses");

            migrationBuilder.DropTable(
                name: "review_items");

            migrationBuilder.DropTable(
                name: "nutritional_assessments");
        }
    }
}
