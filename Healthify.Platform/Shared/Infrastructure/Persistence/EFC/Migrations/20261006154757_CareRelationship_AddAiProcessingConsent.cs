using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class CareRelationship_AddAiProcessingConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consent_ai_decided_at",
                table: "care_links",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "consent_ai_processing",
                table: "care_links",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consent_ai_decided_at",
                table: "care_links");

            migrationBuilder.DropColumn(
                name: "consent_ai_processing",
                table: "care_links");
        }
    }
}
