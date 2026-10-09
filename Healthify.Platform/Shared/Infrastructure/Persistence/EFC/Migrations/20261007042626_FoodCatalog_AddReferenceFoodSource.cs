using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class FoodCatalog_AddReferenceFoodSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ai_generation_id",
                table: "reference_foods",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_verified",
                table: "reference_foods",
                type: "tinyint(1)",
                nullable: false,
                // IN-7: every food that existed before is verified (imported, seeded or added by a practitioner).
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "reference_foods",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Imported");

            migrationBuilder.AddColumn<int>(
                name: "verified_by",
                table: "reference_foods",
                type: "int",
                nullable: true);

            // IN-7. Backfill: the rows a practitioner added are LocalOverride; everything else stays Imported.
            migrationBuilder.Sql("UPDATE `reference_foods` SET `source` = 'LocalOverride' WHERE `is_local_override` = 1;");

            migrationBuilder.CreateIndex(
                name: "ix_reference_foods_source",
                table: "reference_foods",
                column: "source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reference_foods_source",
                table: "reference_foods");

            migrationBuilder.DropColumn(
                name: "ai_generation_id",
                table: "reference_foods");

            migrationBuilder.DropColumn(
                name: "is_verified",
                table: "reference_foods");

            migrationBuilder.DropColumn(
                name: "source",
                table: "reference_foods");

            migrationBuilder.DropColumn(
                name: "verified_by",
                table: "reference_foods");
        }
    }
}
