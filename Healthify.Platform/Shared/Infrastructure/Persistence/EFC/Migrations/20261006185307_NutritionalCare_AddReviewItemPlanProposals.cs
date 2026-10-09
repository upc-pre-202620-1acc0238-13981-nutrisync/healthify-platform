using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class NutritionalCare_AddReviewItemPlanProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recheck_due_at",
                table: "review_items",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recheck_issued_at",
                table: "review_items",
                type: "datetime",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "review_item_plan_proposals",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    review_item_id = table.Column<int>(type: "int", nullable: false),
                    ai_generation_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    proposed_energy_kcal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    proposed_protein_g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    proposed_carb_g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    proposed_fat_g = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    patient_message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    recheck_after_days = table.Column<int>(type: "int", nullable: false),
                    rationale = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    assigned_plan_version = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    added_guidelines = table.Column<string>(type: "json", nullable: false),
                    removed_guidelines = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_review_item_plan_proposals", x => x.id);
                    table.ForeignKey(
                        name: "f_k_review_item_plan_proposals_review_items_review_item_id",
                        column: x => x.review_item_id,
                        principalTable: "review_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_review_items_recheck_due_at",
                table: "review_items",
                column: "recheck_due_at");

            migrationBuilder.CreateIndex(
                name: "ix_review_item_plan_proposals_review_item_id",
                table: "review_item_plan_proposals",
                column: "review_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_item_plan_proposals");

            migrationBuilder.DropIndex(
                name: "ix_review_items_recheck_due_at",
                table: "review_items");

            migrationBuilder.DropColumn(
                name: "recheck_due_at",
                table: "review_items");

            migrationBuilder.DropColumn(
                name: "recheck_issued_at",
                table: "review_items");
        }
    }
}
