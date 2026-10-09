using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class FoodCatalog_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reference_foods",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    local_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    energy_kcal_per_100g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    protein_g_per_100g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    carb_g_per_100g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    fat_g_per_100g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    source_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    is_local_override = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_reference_foods", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_reference_foods_local_name",
                table: "reference_foods",
                column: "local_name");

            migrationBuilder.CreateIndex(
                name: "ix_reference_foods_source_hash",
                table: "reference_foods",
                column: "source_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_foods");
        }
    }
}
