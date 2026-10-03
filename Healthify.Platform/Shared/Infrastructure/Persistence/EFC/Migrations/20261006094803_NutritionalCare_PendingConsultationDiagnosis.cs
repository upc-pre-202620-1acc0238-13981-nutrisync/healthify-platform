using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_PendingConsultationDiagnosis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "discarded_at",
                table: "nutritional_diagnoses",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_consultation_id",
                table: "nutritional_diagnoses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_nutritional_diagnoses_pending_consultation_id",
                table: "nutritional_diagnoses",
                column: "pending_consultation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nutritional_diagnoses_pending_consultation_id",
                table: "nutritional_diagnoses");

            migrationBuilder.DropColumn(
                name: "discarded_at",
                table: "nutritional_diagnoses");

            migrationBuilder.DropColumn(
                name: "pending_consultation_id",
                table: "nutritional_diagnoses");
        }
    }
}
