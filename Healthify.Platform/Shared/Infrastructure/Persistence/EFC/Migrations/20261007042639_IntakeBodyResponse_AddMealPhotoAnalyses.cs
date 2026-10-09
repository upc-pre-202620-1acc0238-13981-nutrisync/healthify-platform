using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class IntakeBodyResponse_AddMealPhotoAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "meal_photo_analysis_id",
                table: "diary_entries",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "proposed_ai_generation_id",
                table: "diary_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "meal_photo_analyses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    reference_food_id = table.Column<int>(type: "int", nullable: false),
                    estimated_grams = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(6,4)", nullable: false),
                    ai_generation_id = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    alternatives = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_meal_photo_analyses", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_diary_entries_meal_photo_analysis_id",
                table: "diary_entries",
                column: "meal_photo_analysis_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meal_photo_analyses_expires_at",
                table: "meal_photo_analyses",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_meal_photo_analyses_patient_id",
                table: "meal_photo_analyses",
                column: "patient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meal_photo_analyses");

            migrationBuilder.DropIndex(
                name: "ix_diary_entries_meal_photo_analysis_id",
                table: "diary_entries");

            migrationBuilder.DropColumn(
                name: "meal_photo_analysis_id",
                table: "diary_entries");

            migrationBuilder.DropColumn(
                name: "proposed_ai_generation_id",
                table: "diary_entries");
        }
    }
}
