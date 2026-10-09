using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_AddPatientBaselines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "patient_baselines",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    birth_date_estimated = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    biological_sex = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    height_cm = table.Column<decimal>(type: "decimal(5,1)", nullable: false),
                    recorded_by = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    conditions = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_patient_baselines", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_patient_baselines_patient_id",
                table: "patient_baselines",
                column: "patient_id",
                unique: true);

            // Backfill (NC-1): one baseline per patient who already has assessments, built from the
            // latest assessment. The birth date is approximated as assessment date minus the stored
            // age and flagged as estimated; the first edit by a practitioner confirms it. The height
            // comes from the patient's latest clinical measurement. Conditions start empty: the old
            // free-text medical history stays on the assessments that recorded it. A patient with
            // assessments but no measurement gets no baseline (height is required) and shows PAC-0.
            migrationBuilder.Sql(@"
INSERT INTO patient_baselines
    (patient_id, birth_date, birth_date_estimated, biological_sex, height_cm, recorded_by,
     created_at, updated_at, conditions)
SELECT a.patient_id,
       DATE_SUB(DATE(COALESCE(a.created_at, a.closed_at, UTC_TIMESTAMP())), INTERVAL a.age_years YEAR),
       1,
       a.biological_sex,
       ROUND(m.height_cm, 1),
       a.practitioner_id,
       UTC_TIMESTAMP(),
       UTC_TIMESTAMP(),
       JSON_ARRAY()
FROM nutritional_assessments a
JOIN (SELECT patient_id, MAX(id) AS id FROM nutritional_assessments GROUP BY patient_id) latest
    ON latest.id = a.id
JOIN (SELECT na.patient_id, cm.height_cm
      FROM clinical_measurements cm
      JOIN nutritional_assessments na ON na.id = cm.assessment_id
      WHERE cm.id = (SELECT cm2.id
                     FROM clinical_measurements cm2
                     JOIN nutritional_assessments na2 ON na2.id = cm2.assessment_id
                     WHERE na2.patient_id = na.patient_id
                     ORDER BY cm2.taken_at DESC, cm2.id DESC
                     LIMIT 1)) m
    ON m.patient_id = a.patient_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient_baselines");
        }
    }
}
