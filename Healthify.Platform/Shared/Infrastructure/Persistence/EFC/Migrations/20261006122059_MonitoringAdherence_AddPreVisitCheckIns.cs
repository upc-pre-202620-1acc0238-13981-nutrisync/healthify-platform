using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringAdherence_AddPreVisitCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pre_visit_check_ins",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    follow_up_id = table.Column<int>(type: "int", nullable: false),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    practitioner_id = table.Column<int>(type: "int", nullable: false),
                    feeling = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    difficulties = table.Column<string>(type: "json", nullable: false),
                    questions = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_pre_visit_check_ins", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_pre_visit_check_ins_patient_id",
                table: "pre_visit_check_ins",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ux_pre_visit_check_ins_follow_up_id",
                table: "pre_visit_check_ins",
                column: "follow_up_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pre_visit_check_ins");
        }
    }
}
