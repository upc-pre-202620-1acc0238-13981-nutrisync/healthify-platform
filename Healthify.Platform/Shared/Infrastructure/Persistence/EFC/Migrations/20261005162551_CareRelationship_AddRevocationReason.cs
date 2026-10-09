using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class CareRelationship_AddRevocationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "revocation_reason",
                table: "care_links",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            // Backfill (CR-1): before this column existed, withdrawing consent was the only path
            // that revoked a care link.
            migrationBuilder.Sql(
                "UPDATE care_links SET revocation_reason = 'ConsentWithdrawn' WHERE revoked_at IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revocation_reason",
                table: "care_links");
        }
    }
}
