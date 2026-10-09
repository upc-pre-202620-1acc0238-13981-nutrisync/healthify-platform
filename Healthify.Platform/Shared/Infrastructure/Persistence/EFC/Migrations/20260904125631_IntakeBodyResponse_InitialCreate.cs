using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class IntakeBodyResponse_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "active_targets_caches",
                columns: table => new
                {
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    plan_version = table.Column<int>(type: "int", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    energy_kcal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    protein_g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    carb_g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    fat_g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    refreshed_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    guidelines = table.Column<string>(type: "json", nullable: false),
                    restrictions = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_active_targets_caches", x => x.patient_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "diary_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    local_timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    local_utc_offset_minutes = table.Column<int>(type: "int", nullable: false),
                    provenance = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    photo_ref = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    proposed_reference_food_id = table.Column<int>(type: "int", nullable: true),
                    proposed_portion_grams = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    proposed_confidence = table.Column<decimal>(type: "decimal(6,4)", nullable: true),
                    proposed_estimated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    confirmed_reference_food_id = table.Column<int>(type: "int", nullable: true),
                    confirmed_portion_grams = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    sync_state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    client_entry_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_diary_entries", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "self_weigh_ins",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    value_kg = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    local_timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    local_utc_offset_minutes = table.Column<int>(type: "int", nullable: false),
                    protocol_fasted_state = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    protocol_same_time_of_day = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    protocol_same_scale = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_self_weigh_ins", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "weight_trends",
                columns: table => new
                {
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    window_size = table.Column<int>(type: "int", nullable: false),
                    last_recalculated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    points = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_weight_trends", x => x.patient_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_diary_entries_client_entry_id",
                table: "diary_entries",
                column: "client_entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diary_entries_local_timestamp",
                table: "diary_entries",
                column: "local_timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_diary_entries_patient_id",
                table: "diary_entries",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_self_weigh_ins_patient_id",
                table: "self_weigh_ins",
                column: "patient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "active_targets_caches");

            migrationBuilder.DropTable(
                name: "diary_entries");

            migrationBuilder.DropTable(
                name: "self_weigh_ins");

            migrationBuilder.DropTable(
                name: "weight_trends");
        }
    }
}
