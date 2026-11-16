using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class Monitoring_AddFollowUpReminderLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "follow_up_reminder_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    follow_up_id = table.Column<int>(type: "int", nullable: false),
                    recipient = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false),
                    lead_minutes = table.Column<int>(type: "int", nullable: false),
                    visit_moment = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_follow_up_reminder_logs", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_follow_up_reminder_logs_reminder",
                table: "follow_up_reminder_logs",
                columns: new[] { "follow_up_id", "recipient", "lead_minutes", "visit_moment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "follow_up_reminder_logs");
        }
    }
}
