using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringAdherence_AddWeeklySummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "weekly_summaries",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    week_start = table.Column<DateTime>(type: "date", nullable: false),
                    week_end = table.Column<DateTime>(type: "date", nullable: false),
                    headline = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    ai_generation_id = table.Column<long>(type: "bigint", nullable: false),
                    language = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    compliance_facts = table.Column<string>(type: "json", nullable: false),
                    watch_out = table.Column<string>(type: "json", nullable: false),
                    went_well = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_weekly_summaries", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_weekly_summaries_generated_at",
                table: "weekly_summaries",
                column: "generated_at");

            migrationBuilder.CreateIndex(
                name: "ux_weekly_summaries_patient_id_week_start",
                table: "weekly_summaries",
                columns: new[] { "patient_id", "week_start" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "weekly_summaries");
        }
    }
}
