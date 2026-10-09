using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class Shared_AddAiGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_generations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    feature = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    subject_patient_id = table.Column<int>(type: "int", nullable: false),
                    requested_by_user_id = table.Column<int>(type: "int", nullable: true),
                    prompt_version = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    model = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    input_hash = table.Column<string>(type: "char(64)", nullable: true),
                    output_json = table.Column<string>(type: "json", nullable: true),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    error_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                    input_tokens = table.Column<int>(type: "int", nullable: false),
                    output_tokens = table.Column<int>(type: "int", nullable: false),
                    latency_ms = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_ai_generations", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ai_generations_expires_at",
                table: "ai_generations",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_ai_generations_requested_by_user_id_feature_created_at",
                table: "ai_generations",
                columns: new[] { "requested_by_user_id", "feature", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_generations_subject_patient_id_feature_created_at",
                table: "ai_generations",
                columns: new[] { "subject_patient_id", "feature", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_generations");
        }
    }
}
