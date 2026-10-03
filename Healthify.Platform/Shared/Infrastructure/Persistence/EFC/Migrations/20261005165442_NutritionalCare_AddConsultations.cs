using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_AddConsultations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consultations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    practitioner_id = table.Column<int>(type: "int", nullable: false),
                    scheduled_follow_up_id = table.Column<int>(type: "int", nullable: true),
                    current_step = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    assessment_id = table.Column<int>(type: "int", nullable: true),
                    diagnosis_id = table.Column<int>(type: "int", nullable: true),
                    plan_id = table.Column<int>(type: "int", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    last_saved_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    published_plan_version = table.Column<int>(type: "int", nullable: true),
                    is_first_consultation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    in_progress_patient_id = table.Column<int>(type: "int", nullable: true, computedColumnSql: "IF(`state` = 'InProgress', `patient_id`, NULL)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_consultations", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_consultations_in_progress_patient_id",
                table: "consultations",
                column: "in_progress_patient_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consultations_patient_id_state",
                table: "consultations",
                columns: new[] { "patient_id", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consultations");
        }
    }
}
