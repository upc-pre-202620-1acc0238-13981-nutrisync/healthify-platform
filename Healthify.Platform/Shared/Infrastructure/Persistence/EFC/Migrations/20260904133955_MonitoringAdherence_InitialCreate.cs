using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringAdherence_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consistency_indices",
                columns: table => new
                {
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    value = table.Column<decimal>(type: "decimal(12,4)", nullable: false),
                    state = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    first_flagged_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    shown_to_patient_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    alert_since_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    last_recomputed_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_consistency_indices", x => x.patient_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    window_id = table.Column<int>(type: "int", nullable: false),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    magnitude_relative_value = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    magnitude_energy_kcal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    direction = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    is_sustained = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sustained_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    logged_days_considered = table.Column<int>(type: "int", nullable: false),
                    deviating_days_considered = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_deviations", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "evaluation_windows",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    care_link_id = table.Column<int>(type: "int", nullable: false),
                    window_days = table.Column<int>(type: "int", nullable: false),
                    from_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    to_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    last_logging_gap_flagged_on = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_patient_reminded_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    anthropometry_series = table.Column<string>(type: "json", nullable: false),
                    daily_compliance_series = table.Column<string>(type: "json", nullable: false),
                    targets_snapshots = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_evaluation_windows", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "referrals",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    specialty = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    issued_by = table.Column<int>(type: "int", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_referrals", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "scheduled_follow_ups",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    practitioner_id = table.Column<int>(type: "int", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    missed_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_scheduled_follow_ups", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_consistency_indices_state",
                table: "consistency_indices",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_deviations_patient_id",
                table: "deviations",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_deviations_window_id",
                table: "deviations",
                column: "window_id");

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_windows_patient_id",
                table: "evaluation_windows",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_windows_state",
                table: "evaluation_windows",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_referrals_patient_id",
                table: "referrals",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_follow_ups_patient_id",
                table: "scheduled_follow_ups",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_follow_ups_practitioner_id",
                table: "scheduled_follow_ups",
                column: "practitioner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consistency_indices");

            migrationBuilder.DropTable(
                name: "deviations");

            migrationBuilder.DropTable(
                name: "evaluation_windows");

            migrationBuilder.DropTable(
                name: "referrals");

            migrationBuilder.DropTable(
                name: "scheduled_follow_ups");
        }
    }
}
