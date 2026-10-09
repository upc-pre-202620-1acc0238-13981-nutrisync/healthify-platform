using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class Iam_AddRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "previous_refresh_token_hash",
                table: "user_sessions",
                type: "char(64)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refresh_token_expires_at",
                table: "user_sessions",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refresh_token_hash",
                table: "user_sessions",
                type: "char(64)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_previous_refresh_token_hash",
                table: "user_sessions",
                column: "previous_refresh_token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_refresh_token_hash",
                table: "user_sessions",
                column: "refresh_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_sessions_previous_refresh_token_hash",
                table: "user_sessions");

            migrationBuilder.DropIndex(
                name: "ix_user_sessions_refresh_token_hash",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "previous_refresh_token_hash",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "refresh_token_expires_at",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "refresh_token_hash",
                table: "user_sessions");
        }
    }
}
