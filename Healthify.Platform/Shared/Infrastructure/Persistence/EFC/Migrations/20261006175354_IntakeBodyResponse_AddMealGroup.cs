using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class IntakeBodyResponse_AddMealGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "meal_group_id",
                table: "diary_entries",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "diary_entries",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_diary_entries_meal_group_id",
                table: "diary_entries",
                column: "meal_group_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_diary_entries_meal_group_id",
                table: "diary_entries");

            migrationBuilder.DropColumn(
                name: "meal_group_id",
                table: "diary_entries");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "diary_entries");
        }
    }
}
