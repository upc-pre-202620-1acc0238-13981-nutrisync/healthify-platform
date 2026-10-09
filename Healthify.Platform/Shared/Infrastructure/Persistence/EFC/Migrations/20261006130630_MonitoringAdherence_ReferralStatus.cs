using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringAdherence_ReferralStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                table: "referrals",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "referrals",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Open");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closed_at",
                table: "referrals");

            migrationBuilder.DropColumn(
                name: "status",
                table: "referrals");
        }
    }
}
