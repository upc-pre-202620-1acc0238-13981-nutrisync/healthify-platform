using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_ConsultationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "discarded_at",
                table: "nutrition_plans",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "abandoned_at",
                table: "consultations",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "consultations",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "publication_draft",
                table: "consultations",
                type: "json",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "discarded_at",
                table: "nutrition_plans");

            migrationBuilder.DropColumn(
                name: "abandoned_at",
                table: "consultations");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "consultations");

            migrationBuilder.DropColumn(
                name: "publication_draft",
                table: "consultations");
        }
    }
}
