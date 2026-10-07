using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringAdherence_FollowUpPreparationAndStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MA-2. Every visit scheduled before this change was in person and had no preparation
            // instructions, which is exactly what the two defaults say.
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "scheduled_follow_ups",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "scheduled_follow_ups",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "scheduled_follow_ups",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "completed_by_consultation_id",
                table: "scheduled_follow_ups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "modality",
                table: "scheduled_follow_ups",
                type: "varchar(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "InPerson");

            migrationBuilder.AddColumn<string>(
                name: "preparation",
                table: "scheduled_follow_ups",
                type: "json",
                nullable: false,
                defaultValueSql: "('[]')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "scheduled_follow_ups");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "scheduled_follow_ups");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "scheduled_follow_ups");

            migrationBuilder.DropColumn(
                name: "completed_by_consultation_id",
                table: "scheduled_follow_ups");

            migrationBuilder.DropColumn(
                name: "modality",
                table: "scheduled_follow_ups");

            migrationBuilder.DropColumn(
                name: "preparation",
                table: "scheduled_follow_ups");
        }
    }
}
