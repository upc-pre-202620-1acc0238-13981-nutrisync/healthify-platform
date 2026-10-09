using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class Iam_AddPersonName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "family_names",
                table: "users",
                type: "varchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "given_names",
                table: "users",
                type: "varchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            // IAM-1 backfill. Accounts created before the names existed get the local part of their email as
            // given names and an empty family name, so every listing has something to show until the person
            // edits it. New accounts always write both parts.
            migrationBuilder.Sql(
                "UPDATE `users` SET `given_names` = LEFT(SUBSTRING_INDEX(`email`, '@', 1), 80) " +
                "WHERE `given_names` = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "family_names",
                table: "users");

            migrationBuilder.DropColumn(
                name: "given_names",
                table: "users");
        }
    }
}
