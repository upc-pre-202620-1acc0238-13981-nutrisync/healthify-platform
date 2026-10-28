using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class Iam_AddPasswordResetVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reset_token_expires_at",
                table: "password_reset_codes",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reset_token_hash",
                table: "password_reset_codes",
                type: "char(64)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at",
                table: "password_reset_codes",
                type: "datetime",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_codes_reset_token_hash",
                table: "password_reset_codes",
                column: "reset_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_password_reset_codes_reset_token_hash",
                table: "password_reset_codes");

            migrationBuilder.DropColumn(
                name: "reset_token_expires_at",
                table: "password_reset_codes");

            migrationBuilder.DropColumn(
                name: "reset_token_hash",
                table: "password_reset_codes");

            migrationBuilder.DropColumn(
                name: "verified_at",
                table: "password_reset_codes");
        }
    }
}
